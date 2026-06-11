using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Helpers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LabelDesigner.Services;

public static class PrintService
{
    public static void Print(LabelTemplate template, Dictionary<string, string> fields,
        string? printerName = null, int copies = 1,
        bool allowFallbackPrinter = true, bool validate = true, string source = "Manual",
        string? printedBy = null)
    {
        if (copies <= 0) return;   // qty = 0 means "don't print" — never default to 1

        // Pre-print validation: a barcode that can't encode must STOP the job (and name itself),
        // never silently print a blank where the scannable code should be.
        if (validate)
        {
            var errors = Validate(template, fields);
            if (errors.Count > 0)
            {
                LogService.Warn($"Print blocked for '{template.Name}': {string.Join("; ", errors)}");
                throw new LabelValidationException(errors);
            }
        }

        // Resolve + verify the printer BEFORE reserving serials, so an offline/missing printer never
        // burns a serial range or reports a phantom "printed".
        var queue = GetPrintQueue(printerName ?? template.PrinterProfile.PrinterName, allowFallbackPrinter);
        CheckPrinterReady(queue);

        // Reserve the serial range up front (crash-safe): if the batch dies partway, the counter has
        // already advanced, so a retry gets a fresh range — a GAP, never a duplicate.
        var reserved   = SerialCounterStore.Reserve(template, copies);
        var constants  = DataSourceResolver.ResolveConstants(template);
        var serialPlan = BuildSerialPlan(template, reserved);

        var perLabel = new List<Dictionary<string, string>>(copies);
        for (int i = 0; i < copies; i++)
        {
            var f = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in DataSourceResolver.Resolve(template, i, reserved))
                f.TryAdd(kv.Key, kv.Value);   // caller fields win over computed ones
            perLabel.Add(f);
        }

        PrintResolved(queue, template, perLabel, source, fields, constants, serialPlan, printedBy);
    }

    /// <summary>
    /// Reprints a recorded run EXACTLY — same serial IDs and same date/values as the original — by
    /// replaying the stored snapshot. Does NOT reserve or advance the counter (so recovering jammed
    /// labels never mints new carton IDs). Defaults to NOT falling back to another printer.
    /// </summary>
    public static int Reprint(LabelTemplate template, PrintHistoryEntry entry,
        string? printerName = null, bool allowFallbackPrinter = false, string? printedBy = null)
    {
        int qty = Math.Max(1, entry.ActualPrinted > 0 ? entry.ActualPrinted : entry.Qty);
        var perLabel = new List<Dictionary<string, string>>(qty);
        for (int i = 0; i < qty; i++)
        {
            var f = new Dictionary<string, string>(entry.Fields, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in entry.ResolvedConstants) f[kv.Key] = kv.Value;        // original date/values
            foreach (var sp in entry.SerialPlan)                                     // exact original serials
                f[sp.Name] = SerialFormatting.Format(sp.Base + (long)i * Math.Max(1, sp.Increment),
                    sp.Prefix, sp.Suffix, sp.Radix <= 0 ? 10 : sp.Radix, sp.PadWidth, sp.Format);
            perLabel.Add(f);
        }

        var queue = GetPrintQueue(printerName ?? entry.Printer ?? template.PrinterProfile.PrinterName, allowFallbackPrinter);
        CheckPrinterReady(queue);
        return PrintResolved(queue, template, perLabel, "Reprint", entry.Fields, entry.ResolvedConstants, entry.SerialPlan, printedBy);
    }

    /// <summary>
    /// Prints ONE proof label for registration/darkness/scannability checks WITHOUT consuming a
    /// Continuous serial — it uses the current base (GetBase) and never reserves/advances. Recorded
    /// as source "Test" so it's auditable but obviously not a production run.
    /// </summary>
    public static void PrintTest(LabelTemplate template, Dictionary<string, string> fields, string? printerName = null,
        string? printedBy = null)
    {
        var errors = Validate(template, fields);
        if (errors.Count > 0)
        {
            LogService.Warn($"Test print blocked for '{template.Name}': {string.Join("; ", errors)}");
            throw new LabelValidationException(errors);
        }

        var queue = GetPrintQueue(printerName ?? template.PrinterProfile.PrinterName, allowFallback: false);
        CheckPrinterReady(queue);

        var constants = DataSourceResolver.ResolveConstants(template);
        var f = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in DataSourceResolver.Resolve(template, 0, null)) // null reserved → GetBase, no advance
            f.TryAdd(kv.Key, kv.Value);

        PrintResolved(queue, template, new List<Dictionary<string, string>> { f },
            "Test", fields, constants, new List<SerialPlanItem>(), printedBy);
    }

    /// <summary>
    /// The print engine: sends each already-resolved per-label field dict to the spooler and records
    /// ONE history entry with the ACTUAL printed count (even if a jam aborts the batch partway).
    /// </summary>
    private static int PrintResolved(PrintQueue queue, LabelTemplate template,
        IReadOnlyList<Dictionary<string, string>> perLabel, string source,
        Dictionary<string, string> historyFields, Dictionary<string, string> resolvedConstants,
        List<SerialPlanItem> serialPlan, string? printedBy = null)
    {
        if (perLabel.Count == 0) return 0;
        printedBy ??= Environment.UserName;

        // Native ZPL backend (opt-in): emit ZPL II and send raw, bypassing GDI entirely.
        if (template.PrinterProfile.OutputMode == PrintBackend.Zpl)
            return PrintZpl(queue, template, perLabel, source, historyFields, resolvedConstants, serialPlan, printedBy);

        var ticket = queue.DefaultPrintTicket;
        ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.Unknown, template.WidthPx, template.HeightPx);
        try { ticket.PageResolution = new PageResolution(template.Dpi, template.Dpi); }
        catch (Exception ex) { LogService.Warn($"Driver rejected PageResolution {template.Dpi}: {ex.Message}"); }

        // Registration offset is applied to the visual (BuildPrintVisual). Darkness/speed/media are
        // recorded here; they take effect natively on the ZPL path — on the GDI path they must be set
        // in the Zebra driver preferences (PrintTicket can't carry them across drivers).
        var prof = template.PrinterProfile;
        if (prof.Darkness.HasValue || prof.SpeedIps.HasValue || prof.MediaType != ThermalMediaType.GapLabel ||
            prof.LabelOffsetXMm != 0 || prof.LabelOffsetYMm != 0)
            LogService.Info($"Printer profile '{template.Name}': darkness={(prof.Darkness?.ToString() ?? "driver")}, " +
                $"speed={(prof.SpeedIps?.ToString() ?? "driver")}ips, media={prof.MediaType}, " +
                $"offset=({prof.LabelOffsetXMm},{prof.LabelOffsetYMm})mm.");

        var dialog = new PrintDialog { PrintQueue = queue, PrintTicket = ticket };
        int printed = 0;
        try
        {
            foreach (var fields in perLabel)
            {
                var visual = BuildPrintVisual(template, fields);
                dialog.PrintVisual(visual, template.Name);
                printed++;
            }
        }
        finally
        {
            // Record what ACTUALLY printed, with the snapshot needed for a faithful reprint.
            PrintHistoryService.Record(new PrintHistoryEntry
            {
                TimestampIso      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TemplateName      = template.Name,
                TemplateId        = template.Id.ToString(),
                Printer           = queue.Name,
                Qty               = perLabel.Count,
                ActualPrinted     = printed,
                Source            = source,
                PrintedBy         = printedBy,
                Fields            = new Dictionary<string, string>(historyFields),
                ResolvedConstants = new Dictionary<string, string>(resolvedConstants),
                SerialPlan        = serialPlan
            });
        }
        LogService.Info($"Printed {printed}/{perLabel.Count} '{template.Name}' to '{queue.Name}' @ {template.Dpi} dpi [{source}] by {printedBy}.");
        return printed;
    }

    /// <summary>ZPL backend: render each label to ZPL II and send raw (TCP 9100 if a NetworkHost is set,
    /// otherwise RAW to the Windows queue). Records history with the same snapshot as the GDI path.</summary>
    private static int PrintZpl(PrintQueue queue, LabelTemplate template,
        IReadOnlyList<Dictionary<string, string>> perLabel, string source,
        Dictionary<string, string> historyFields, Dictionary<string, string> resolvedConstants,
        List<SerialPlanItem> serialPlan, string? printedBy = null)
    {
        printedBy ??= Environment.UserName;
        var prof = template.PrinterProfile;
        int printed = 0;
        try
        {
            foreach (var fields in perLabel)
            {
                var merged = MergeFields(template, fields); // apply DS / defaults / format / formulas
                var zpl = ZplRenderer.Render(template, merged, (el, f) => RenderElementToMono(el, f, template));
                var bytes = System.Text.Encoding.UTF8.GetBytes(zpl);

                if (!string.IsNullOrWhiteSpace(prof.NetworkHost))
                    RawPrinter.SendToTcp(prof.NetworkHost!, prof.NetworkPort, bytes);
                else
                    RawPrinter.SendToQueue(queue.Name, template.Name, bytes);
                printed++;
            }
        }
        finally
        {
            PrintHistoryService.Record(new PrintHistoryEntry
            {
                TimestampIso      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TemplateName      = template.Name,
                TemplateId        = template.Id.ToString(),
                Printer           = string.IsNullOrWhiteSpace(prof.NetworkHost) ? queue.Name : $"{prof.NetworkHost}:{prof.NetworkPort}",
                Qty               = perLabel.Count,
                ActualPrinted     = printed,
                Source            = source,
                PrintedBy         = printedBy,
                Fields            = new Dictionary<string, string>(historyFields),
                ResolvedConstants = new Dictionary<string, string>(resolvedConstants),
                SerialPlan        = serialPlan
            });
        }
        LogService.Info($"Printed {printed}/{perLabel.Count} '{template.Name}' via ZPL @ {template.Dpi} dpi [{source}] by {printedBy}.");
        return printed;
    }

    /// <summary>Renders one label to a ZPL II string (for export/preview). Applies the same data merge as printing.</summary>
    public static string RenderZpl(LabelTemplate template, Dictionary<string, string> fields)
    {
        var merged = MergeFields(template, fields);
        return ZplRenderer.Render(template, merged, (el, f) => RenderElementToMono(el, f, template));
    }

    /// <summary>Rasterises a single element to a 1-bpp ZPL bitmap (for ^GFA fallback: images, tables,
    /// non-rectangular shapes). Reuses the same Build* visuals as the GDI path so it matches the preview.</summary>
    private static ZplBitmap? RenderElementToMono(LabelElement element, Dictionary<string, string> fields, LabelTemplate template)
    {
        UIElement? ui = element switch
        {
            TextElement    te => BuildText(te, fields),
            BarcodeElement be => BuildBarcode(be, fields, template.Dpi / 96.0),
            ImageElement   ie => BuildImage(ie, fields),
            ShapeElement   se => BuildShape(se),
            TableElement   te => BuildTable(te, fields),
            _ => null
        };
        if (ui is not FrameworkElement fe) return null;
        try
        {
            fe.Width = element.Width;
            fe.Height = element.Height;
            fe.Measure(new Size(element.Width, element.Height));
            fe.Arrange(new Rect(0, 0, element.Width, element.Height));
            fe.UpdateLayout();
            var rtb = BitmapHelper.RenderVisual(fe, element.Width, element.Height, template.Dpi);
            return BitmapHelper.ToZplMono(rtb);
        }
        catch (Exception ex)
        {
            LogService.Warn($"ZPL raster of {element.Type} failed: {ex.Message}");
            return null;
        }
    }

    private static List<SerialPlanItem> BuildSerialPlan(LabelTemplate t, IReadOnlyDictionary<Guid, long> reserved) =>
        t.DataSources
            .Where(d => d.Type == DataSourceType.Serial && reserved.ContainsKey(d.Id))
            .Select(d => new SerialPlanItem
            {
                Name = d.Name, Base = reserved[d.Id], Increment = Math.Max(1, d.Increment), Format = d.Format,
                Prefix = d.SerialPrefix, Suffix = d.SerialSuffix, Radix = d.SerialRadix, PadWidth = d.SerialPadWidth
            })
            .ToList();

    /// <summary>Validates every row of a batch up front so a bad row can't abort a half-printed run.
    /// Errors are prefixed with the 1-based row number.</summary>
    public static IReadOnlyList<string> ValidateBatch(LabelTemplate template, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var errors = new List<string>();
        for (int i = 0; i < rows.Count; i++)
            foreach (var e in Validate(template, rows[i]))
                errors.Add($"Row {i + 1}: {e}");
        return errors;
    }

    /// <summary>Per-row validation, index-aligned with <paramref name="rows"/> (empty list = row OK).
    /// Backs skip-with-reason mode: callers print the clean rows and REPORT the dirty ones explicitly.
    /// <see cref="ValidateBatch"/> stays the all-or-nothing aggregate.</summary>
    public static List<List<string>> ValidateRows(LabelTemplate template, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var results = new List<List<string>>(rows.Count);
        foreach (var row in rows)
            results.Add(Validate(template, row).ToList());
        return results;
    }

    /// <summary>Throws PrinterOfflineException when the queue reports a hard problem. Best-effort —
    /// if the driver doesn't report status, we don't block (many thermal drivers don't).</summary>
    private static void CheckPrinterReady(PrintQueue queue)
    {
        try
        {
            queue.Refresh();
            const PrintQueueStatus problems =
                PrintQueueStatus.Offline | PrintQueueStatus.Error | PrintQueueStatus.PaperOut |
                PrintQueueStatus.PaperJam | PrintQueueStatus.DoorOpen | PrintQueueStatus.NotAvailable |
                PrintQueueStatus.NoToner | PrintQueueStatus.UserIntervention |
                PrintQueueStatus.OutOfMemory | PrintQueueStatus.PaperProblem;
            var st = queue.QueueStatus;
            if ((st & problems) != 0)
            {
                LogService.Warn($"Printer '{queue.Name}' not ready: {st}");
                throw new PrinterOfflineException(queue.Name, st.ToString());
            }
        }
        catch (PrinterOfflineException) { throw; }
        catch (Exception ex) { LogService.Warn($"Could not read status for '{queue.Name}': {ex.Message}"); }
    }

    public static BitmapSource RenderPreview(LabelTemplate template, Dictionary<string, string> fields, double dpi = 96)
    {
        // Preview now renders through the EXACT same builder as Print, so what the operator
        // approves is what prints (render parity). Barcodes are built at device-dot resolution.
        var visual = BuildPrintVisual(template, fields);
        visual.Measure(new Size(template.WidthPx, template.HeightPx));
        visual.Arrange(new Rect(0, 0, template.WidthPx, template.HeightPx));
        visual.UpdateLayout();
        return BitmapHelper.RenderVisual(visual, template.WidthPx, template.HeightPx, dpi);
    }

    // ─── Build print visual ────────────────────────────────────────────────
    private static UIElement BuildPrintVisual(LabelTemplate template, Dictionary<string, string> fields)
    {
        // Merge in computed data-source values (existing fields win).
        fields = MergeFields(template, fields);

        // Barcodes/images are rasterised at the printer's device dots so module widths are whole
        // dots (scannable), not the product of resampling a 96-DPI bitmap up to 203/300 DPI.
        double deviceScale = template.Dpi / 96.0;

        var canvas = new Canvas
        {
            Width  = template.WidthPx,
            Height = template.HeightPx,
            Background = ParseBrush(template.BackgroundColor)
        };

        // Apply the printer profile's registration offset (mm → 96-DPI px) so a consistently
        // mis-aligned label can be nudged into place without editing every element.
        var profile = template.PrinterProfile;
        if (profile.LabelOffsetXMm != 0 || profile.LabelOffsetYMm != 0)
            canvas.RenderTransform = new TranslateTransform(
                LabelTemplate.MmToPixels(profile.LabelOffsetXMm),
                LabelTemplate.MmToPixels(profile.LabelOffsetYMm));

        // Build layer → order index lookup (index 0 = top = frontmost)
        var layerCount = template.Layers.Count;
        var layerIndex = template.Layers
            .Select((l, i) => (l.Id, i))
            .ToDictionary(x => x.Id, x => x.i);

        // Sort so elements on top layers render last (painters order)
        var sorted = template.Elements.OrderBy(e =>
        {
            int layerBase = e.LayerId.HasValue && layerIndex.TryGetValue(e.LayerId.Value, out var li)
                ? (layerCount - li) * 1000
                : 0;
            return layerBase + e.ZIndex;
        });

        foreach (var element in sorted)
        {
            // Skip elements on layers whose print condition fails
            if (element.LayerId.HasValue)
            {
                var layer = template.Layers.FirstOrDefault(l => l.Id == element.LayerId);
                if (layer != null && !string.IsNullOrWhiteSpace(layer.PrintCondition) &&
                    !ConditionEvaluator.Evaluate(layer.PrintCondition, fields))
                    continue;
            }

            if (!ConditionEvaluator.Evaluate(element.PrintCondition, fields)) continue;

            UIElement? ui = element switch
            {
                TextElement    te => BuildText(te, fields),
                BarcodeElement be => BuildBarcode(be, fields, deviceScale),
                ImageElement   ie => BuildImage(ie, fields),
                ShapeElement   se => BuildShape(se),
                TableElement   te => BuildTable(te, fields),
                _ => null
            };

            if (ui == null) continue;

            Canvas.SetLeft(ui, element.X);
            Canvas.SetTop(ui, element.Y);
            if (ui is FrameworkElement fe)
            {
                fe.Width = element.Width;
                fe.Height = element.Height;
                // Rotate about the element's centre — identical to the design canvas (render parity).
                if (element.Rotation != 0)
                {
                    fe.RenderTransformOrigin = new Point(0.5, 0.5);
                    fe.RenderTransform = new RotateTransform(element.Rotation);
                }
            }
            canvas.Children.Add(ui);
        }

        return canvas;
    }

    /// <summary>Merges computed data-source values into the field dict (existing fields take priority).</summary>
    private static Dictionary<string, string> MergeFields(LabelTemplate template, Dictionary<string, string> fields)
    {
        var merged = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase);
        if (template.DataSources.Count > 0)
            foreach (var kv in DataSourceResolver.Resolve(template.DataSources))
                merged.TryAdd(kv.Key, kv.Value);
        ApplyFieldDefs(template, merged);
        DataSourceResolver.ApplyFormulas(template.DataSources, merged); // formulas see fields + sources + defaults
        return merged;
    }

    /// <summary>Applies FieldDefinition DefaultValue (when a field is missing/blank) and Format
    /// (Number/Date) so the metadata the back office set in Manage Fields actually takes effect.
    /// Skips data-source names (they format themselves).</summary>
    private static void ApplyFieldDefs(LabelTemplate template, Dictionary<string, string> dict)
    {
        if (template.FieldDefinitions.Count == 0) return;
        var dsNames = new HashSet<string>(template.DataSources.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var fd in template.FieldDefinitions)
        {
            if (string.IsNullOrWhiteSpace(fd.Name) || dsNames.Contains(fd.Name)) continue;

            bool has = dict.TryGetValue(fd.Name, out var v) && !string.IsNullOrEmpty(v);
            if (!has && !string.IsNullOrEmpty(fd.DefaultValue)) { v = fd.DefaultValue; dict[fd.Name] = v; has = true; }
            if (has && !string.IsNullOrWhiteSpace(fd.Format)) dict[fd.Name] = ApplyFormat(fd, v!);
        }
    }

    private static string ApplyFormat(FieldDefinition fd, string value)
    {
        try
        {
            switch (fd.DataType)
            {
                case FieldDataType.Number:
                    if (double.TryParse(value, System.Globalization.NumberStyles.Any, CultureInfo.CurrentCulture, out var n) ||
                        double.TryParse(value, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out n))
                        return n.ToString(fd.Format, CultureInfo.CurrentCulture);
                    return value;
                case FieldDataType.Date:
                    if (DateTime.TryParse(value, out var dt)) return dt.ToString(fd.Format, CultureInfo.CurrentCulture);
                    return value;
                default:
                    return value; // text format is ambiguous → leave as-is
            }
        }
        catch { return value; }
    }

    /// <summary>
    /// Pre-print validation. Returns one message per problem (currently: un-encodable barcodes on
    /// elements that would actually print under the current data/conditions). Empty list = good to print.
    /// </summary>
    public static IReadOnlyList<string> Validate(LabelTemplate template, Dictionary<string, string> fields)
    {
        var merged = MergeFields(template, fields);
        var errors = new List<string>();

        // Field rules (required / length / range / allowed-values / pattern) — enforced for ALL
        // paths (manual, Excel/CSV batch, IPC) so present-but-wrong data is caught before printing.
        if (template.FieldDefinitions.Count > 0)
        {
            var dsNames = new HashSet<string>(template.DataSources.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var fd in template.FieldDefinitions)
            {
                if (string.IsNullOrWhiteSpace(fd.Name) || dsNames.Contains(fd.Name)) continue;
                merged.TryGetValue(fd.Name, out var val);
                var err = FieldValidator.Validate(fd, val);
                if (err != null) errors.Add(err);
            }
        }

        foreach (var be in template.Elements.OfType<BarcodeElement>())
        {
            // Mirror BuildPrintVisual's skip logic so we never block on a barcode that won't print.
            if (!ConditionEvaluator.Evaluate(be.PrintCondition, merged)) continue;
            if (be.LayerId.HasValue)
            {
                var layer = template.Layers.FirstOrDefault(l => l.Id == be.LayerId);
                if (layer != null && !string.IsNullOrWhiteSpace(layer.PrintCondition) &&
                    !ConditionEvaluator.Evaluate(layer.PrintCondition, merged))
                    continue;
            }
            var value = be.BoundField != null && merged.TryGetValue(be.BoundField, out var fv) ? fv : be.BarcodeValue;
            var err = BarcodeValidator.Validate(be.Format, value);
            if (err != null)
                errors.Add($"Barcode \"{be.BoundField ?? be.BarcodeValue}\" ({be.Format}): {err} (value: \"{value}\")");
        }

        // An image that would print must resolve to a real file (data-driven or static) — never
        // silently print a blank where the logo/icon should be.
        foreach (var ie in template.Elements.OfType<ImageElement>())
        {
            if (!ConditionEvaluator.Evaluate(ie.PrintCondition, merged)) continue;
            if (ie.LayerId.HasValue)
            {
                var layer = template.Layers.FirstOrDefault(l => l.Id == ie.LayerId);
                if (layer != null && !string.IsNullOrWhiteSpace(layer.PrintCondition) &&
                    !ConditionEvaluator.Evaluate(layer.PrintCondition, merged))
                    continue;
            }
            bool intended = !string.IsNullOrEmpty(ie.BoundField) || !string.IsNullOrWhiteSpace(ie.ImagePath);
            if (!intended) continue;
            var path = ResolveImagePath(ie, merged);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                errors.Add($"Image {(ie.BoundField != null ? $"[{ie.BoundField}] " : "")}file not found: \"{path}\"");
        }

        return errors;
    }

    private static string Substitute(string text, Dictionary<string, string> fields) =>
        // Token names may contain spaces/punctuation (Excel headers, data-source names like
        // "Best Before"), so match anything up to the closing braces — \w+ silently skipped them
        // and the literal "{{Best Before}}" printed while the canvas preview substituted it.
        Regex.Replace(text, @"\{\{\s*([^{}]+?)\s*\}\}", m =>
            // Unmatched tokens are replaced with empty string so labels don't print "{{Foo}}".
            fields.TryGetValue(m.Groups[1].Value, out var v) ? v : "");

    private static UIElement BuildText(TextElement te, Dictionary<string, string> fields)
    {
        string value = te.BoundField != null && fields.TryGetValue(te.BoundField, out var fv)
            ? fv : Substitute(te.Text, fields);

        var tb = new TextBlock
        {
            Text         = value,
            FontFamily   = new FontFamily(te.FontFamily),
            FontSize     = te.FontSize,
            FontWeight   = te.Bold   ? FontWeights.Bold   : FontWeights.Normal,
            FontStyle    = te.Italic ? FontStyles.Italic  : FontStyles.Normal,
            Foreground   = ParseBrush(te.Color),
            TextAlignment = te.Alignment switch
            {
                TextAlignmentOption.Center => TextAlignment.Center,
                TextAlignmentOption.Right  => TextAlignment.Right,
                _ => TextAlignment.Left
            },
            // Wrap iff the element is in multi-line mode — matches what the canvas shows.
            TextWrapping = te.MultiLine ? TextWrapping.Wrap : TextWrapping.NoWrap
        };
        if (te.Underline) tb.TextDecorations = TextDecorations.Underline;

        // Single-line + FitToBox: scale uniformly via Viewbox (identical to canvas).
        // Multi-line + FitToBox: binary-search the largest font that fits both width and height
        // while wrapping. Multi-line + !FitToBox: render at declared FontSize, let it wrap.
        if (te.FitToBox && !te.MultiLine)
        {
            var vb = new System.Windows.Controls.Viewbox { Stretch = Stretch.Uniform, Child = tb };
            return vb;
        }

        if (te.FitToBox && te.MultiLine)
        {
            double lo = 1, hi = te.FontSize * 3 + 1, best = te.FontSize;
            for (int iter = 0; iter < 16; iter++)
            {
                double mid = (lo + hi) / 2;
                tb.FontSize = mid;
                tb.Measure(new Size(te.Width, double.PositiveInfinity));
                if (tb.DesiredSize.Width <= te.Width && tb.DesiredSize.Height <= te.Height)
                { best = mid; lo = mid; }
                else hi = mid;
            }
            tb.FontSize = best;
        }

        return tb;
    }

    private static UIElement BuildBarcode(BarcodeElement be, Dictionary<string, string> fields, double deviceScale)
    {
        var value = be.BoundField != null && fields.TryGetValue(be.BoundField, out var fv) ? fv : be.BarcodeValue;

        // Validate first so a bad value becomes a visible, blocking error — never a silent blank.
        var error = BarcodeValidator.Validate(be.Format, value);
        if (error == null)
        {
            // Render at the element's exact device-dot size so the printed barcode maps ~1:1 to dots,
            // preserving the integer module widths ZXing produces (no resampling = reliable scanning).
            int renderW = Math.Max(1, (int)Math.Round(be.Width  * deviceScale));
            int renderH = Math.Max(1, (int)Math.Round(be.Height * deviceScale));
            var img = BitmapHelper.GenerateBarcode(value, be.Format, renderW, renderH,
                be.ShowText, be.TextFontFamily, (float)be.TextFontSize,
                qualityMultiplier: 1, errorCorrection: be.ErrorCorrectionLevel);
            if (img != null)
            {
                var image = new Image { Source = img, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                return image;
            }
            error = "could not be encoded";
        }

        LogService.Warn($"Barcode '{be.BoundField ?? be.BarcodeValue}' ({be.Format}) value '{value}': {error}");
        return BuildErrorPlaceholder("⚠ " + error);
    }

    /// <summary>A visible red placeholder so a failed/invalid element is obvious on screen and on paper,
    /// not a silent blank. A real print job is still blocked by Validate() before it reaches here.</summary>
    private static UIElement BuildErrorPlaceholder(string reason) => new System.Windows.Controls.Border
    {
        Background       = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)),
        BorderBrush      = Brushes.Red,
        BorderThickness  = new Thickness(1),
        Child = new TextBlock
        {
            Text                = reason,
            Foreground          = Brushes.Red,
            FontSize            = 8,
            TextWrapping        = TextWrapping.Wrap,
            TextAlignment       = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(1)
        }
    };

    private static UIElement BuildImage(ImageElement ie, Dictionary<string, string> fields)
    {
        var path = ResolveImagePath(ie, fields);
        if (string.IsNullOrWhiteSpace(path))
            return BuildErrorPlaceholder(ie.BoundField != null ? "⚠ no image path" : "⚠ image not set");
        if (!File.Exists(path))
        {
            LogService.Warn($"Image not found: '{path}'.");
            return BuildErrorPlaceholder("⚠ image not found");
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            // Thermal conditioning (grayscale/threshold/dither/invert) — WYSIWYG with the print.
            var conditioned = BitmapHelper.ConditionImage(bmp, ie.RenderMode, ie.Invert, ie.Threshold);
            return new Image
            {
                Source  = conditioned,
                Stretch = ie.MaintainAspectRatio ? Stretch.Uniform : Stretch.Fill,
                Opacity = ie.Opacity
            };
        }
        catch (Exception ex)
        {
            LogService.Warn($"Image '{path}' failed to load: {ex.Message}");
            return BuildErrorPlaceholder("⚠ image error");
        }
    }

    /// <summary>Resolves an image element's path: bound-field value (optionally under a base folder), else the static path.</summary>
    private static string ResolveImagePath(ImageElement ie, Dictionary<string, string> fields)
    {
        if (!string.IsNullOrEmpty(ie.BoundField) && fields.TryGetValue(ie.BoundField, out var v) && !string.IsNullOrWhiteSpace(v))
            return !string.IsNullOrWhiteSpace(ie.ImageBaseFolder) ? Path.Combine(ie.ImageBaseFolder, v) : v;
        return ie.ImagePath;
    }

    private static UIElement BuildShape(ShapeElement se)
    {
        var fill   = ParseBrush(se.FillColor);
        var stroke = ParseBrush(se.StrokeColor);

        return se.ShapeType switch
        {
            ShapeType.Ellipse  => (UIElement)new System.Windows.Shapes.Ellipse
                { Fill = fill, Stroke = stroke, StrokeThickness = se.StrokeThickness },
            ShapeType.Line     => new System.Windows.Shapes.Line
                { X1 = 0,
                  Y1 = se.LineReverseY ? se.Height : 0,
                  X2 = se.Width,
                  Y2 = se.LineReverseY ? 0 : se.Height,
                  Stroke = stroke, StrokeThickness = se.StrokeThickness },
            ShapeType.Triangle => BuildPrintPolygon(fill, stroke, se.StrokeThickness,
                new[] { "50,0", "100,100", "0,100" }),
            ShapeType.Arrow    => BuildPrintPolygon(fill, stroke, se.StrokeThickness,
                new[] { "0,20", "60,20", "60,0", "100,50", "60,100", "60,80", "0,80" }),
            ShapeType.Diamond  => BuildPrintPolygon(fill, stroke, se.StrokeThickness,
                new[] { "50,0", "100,50", "50,100", "0,50" }),
            _ => new System.Windows.Shapes.Rectangle
            {
                Fill = fill, Stroke = stroke, StrokeThickness = se.StrokeThickness,
                RadiusX = se.CornerRadius, RadiusY = se.CornerRadius
            }
        };
    }

    private static UIElement BuildTable(TableElement te, Dictionary<string, string> fields)
    {
        if (te.Columns.Count == 0)
            return new Border();

        var headerFill  = ParseBrush(te.HeaderBackground);
        var cellFill    = ParseBrush(te.CellBackground);
        var borderBrush = ParseBrush(te.BorderColor);
        var bt          = te.BorderThickness;

        // Determine data rows: prefer static rows; fall back to one live-data row
        var dataRows = te.StaticRows.Count > 0
            ? te.StaticRows
            : new List<List<string>>
              {
                  te.Columns.Select(c =>
                  {
                      string v = "";
                      if (!string.IsNullOrEmpty(c.BoundField)) fields.TryGetValue(c.BoundField, out v!);
                      return v ?? "";
                  }).ToList()
              };

        var grid = new Grid { Background = cellFill };

        foreach (var col in te.Columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(col.Width, GridUnitType.Star) });

        int headerRows = te.ShowHeader ? 1 : 0;
        if (te.ShowHeader)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(te.RowHeight) });

        foreach (var _ in dataRows)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(te.RowHeight) });

        // Header row
        if (te.ShowHeader)
        {
            for (int ci = 0; ci < te.Columns.Count; ci++)
            {
                var headerCell = new Border
                {
                    Background      = headerFill,
                    BorderBrush     = borderBrush,
                    BorderThickness = new Thickness(bt)
                };
                headerCell.Child = new TextBlock
                {
                    Text                = te.Columns[ci].Header,
                    FontFamily          = new FontFamily(te.FontFamily),
                    FontSize            = te.HeaderFontSize,
                    FontWeight          = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Margin              = new Thickness(2)
                };
                Grid.SetRow(headerCell, 0);
                Grid.SetColumn(headerCell, ci);
                grid.Children.Add(headerCell);
            }
        }

        // Data rows
        for (int ri = 0; ri < dataRows.Count; ri++)
        {
            var rowData = dataRows[ri];
            int gridRow = headerRows + ri;
            for (int ci = 0; ci < te.Columns.Count; ci++)
            {
                string cellValue = ci < rowData.Count ? rowData[ci] : "";
                // If static rows are empty and there's a bound field, use live data
                if (te.StaticRows.Count == 0 && string.IsNullOrEmpty(cellValue) &&
                    !string.IsNullOrEmpty(te.Columns[ci].BoundField))
                    fields.TryGetValue(te.Columns[ci].BoundField, out cellValue!);

                var dataCell = new Border
                {
                    Background      = cellFill,
                    BorderBrush     = borderBrush,
                    BorderThickness = new Thickness(bt)
                };
                dataCell.Child = new TextBlock
                {
                    Text                = cellValue ?? "",
                    FontFamily          = new FontFamily(te.FontFamily),
                    FontSize            = te.CellFontSize,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                    TextWrapping        = TextWrapping.Wrap,
                    Margin              = new Thickness(2)
                };
                Grid.SetRow(dataCell, gridRow);
                Grid.SetColumn(dataCell, ci);
                grid.Children.Add(dataCell);
            }
        }

        return grid;
    }

    private static System.Windows.Controls.Viewbox BuildPrintPolygon(
        Brush fill, Brush stroke, double strokeThickness, string[] points)
    {
        // Points are authored in a 100×100 logical space (see ShapeType cases above).
        // Use Stretch.Fill so the shape always fills the element's box — non-square
        // elements are *intentionally* stretched (matches the on-canvas designer behaviour).
        var pts = new System.Windows.Media.PointCollection(
            points.Select(p =>
            {
                var xy = p.Split(',');
                double x = 0, y = 0;
                if (xy.Length == 2)
                {
                    double.TryParse(xy[0], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out x);
                    double.TryParse(xy[1], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out y);
                }
                return new System.Windows.Point(x, y);
            }));
        var poly = new System.Windows.Shapes.Polygon
        {
            Points = pts, Fill = fill, Stroke = stroke, StrokeThickness = strokeThickness
        };
        var vb = new System.Windows.Controls.Viewbox { Stretch = Stretch.Fill };
        vb.Child = poly;
        return vb;
    }

    private static Brush ParseBrush(string color)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(color)!; }
        catch { return Brushes.Transparent; }
    }

    /// <summary>
    /// Raised when the caller asked for a printer by name but it wasn't found.
    /// The UI can hook this to surface a warning instead of silently falling back to the default.
    /// </summary>
    public static event Action<string>? PrinterNotFound;

    private static PrintQueue GetPrintQueue(string? name, bool allowFallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return LocalPrintServer.GetDefaultPrintQueue();

        using var server = new LocalPrintServer();
        foreach (PrintQueue q in server.GetPrintQueues())
            if (q.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return q;

        LogService.Warn($"Requested printer '{name}' not found (allowFallback={allowFallback}).");
        PrinterNotFound?.Invoke(name!);

        // For unattended / JaneERP jobs, refuse to divert production labels to the wrong device
        // (e.g. landing on "Microsoft Print to PDF" when the Zebra is offline).
        if (!allowFallback)
            throw new PrinterNotFoundException(name!);

        Debug.WriteLine($"[PrintService] Printer '{name}' not found, falling back to default.");
        return LocalPrintServer.GetDefaultPrintQueue();
    }
}
