using LabelDesigner.Services;
using LabelDesigner.ViewModels;
using System.IO;
using System.Windows;
using Xunit;

namespace LabelDesigner.Tests;

// Regression tests for the 2026-06-11 field-report fixes.

public class MultiLineTextVisibilityTests
{
    [Fact]
    public void PlainBlockShows_ExceptForSingleLineFitToBox()
    {
        var vm = new TextElementViewModel();

        // Default: plain text (renders + wraps like print).
        Assert.Equal(Visibility.Visible, vm.PlainTextVisibility);
        Assert.Equal(Visibility.Collapsed, vm.FitViewboxVisibility);

        // Single-line FitToBox is the ONLY case that uses the Viewbox.
        vm.FitToBox = true;
        Assert.Equal(Visibility.Collapsed, vm.PlainTextVisibility);
        Assert.Equal(Visibility.Visible, vm.FitViewboxVisibility);

        // Multi-line must NEVER go through the Viewbox — it measures with infinite
        // width, so wrapping text inside one never wraps (the reported bug).
        vm.MultiLine = true;
        Assert.Equal(Visibility.Visible, vm.PlainTextVisibility);
        Assert.Equal(Visibility.Collapsed, vm.FitViewboxVisibility);
    }
}

public class AvailableFieldsSyncTests
{
    [Fact]
    public void DataSourceEdits_DoNotRebuildAvailableFields_WhenUnchanged()
    {
        var designer = new DesignerViewModel();
        var el = new TextElementViewModel();
        designer.AddElement(el, recordUndo: false);

        designer.Template.Fields.Add("Flavor");
        designer.SyncAvailableFields();
        Assert.Contains("Flavor", el.AvailableFields);

        // A second sync with identical content must NOT clear+refill the collection —
        // the editable Bound Field ComboBox drops its text on a rebuild.
        bool changed = false;
        el.AvailableFields.CollectionChanged += (_, _) => changed = true;
        designer.SyncAvailableFields();
        Assert.False(changed);
    }
}

public class PrintPreviewJobNavigationTests
{
    private static ExcelRow Row(string name, int qty) =>
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = name }, qty);

    [Fact]
    public void PreviewWalksOnlyRowsThatWillPrint()
    {
        var rows = new[] { Row("skipped", 0), Row("first", 2), Row("second", 3) };
        var vm = new PrintPreviewViewModel(new Core.Models.LabelTemplate(), rows[1].Fields,
            dataQty: 2, allRows: rows);

        Assert.True(vm.PrintAllRecords);                       // all-records is the default with rows
        Assert.Contains("Record 1 of 2", vm.PreviewInfo);      // the qty-0 row is not part of the job
        Assert.Contains("×2", vm.PreviewInfo);

        Assert.True(vm.NextLabelCommand.CanExecute(null));
        vm.NextLabelCommand.Execute(null);
        Assert.Contains("Record 2 of 2", vm.PreviewInfo);
        Assert.Contains("×3", vm.PreviewInfo);
        Assert.False(vm.NextLabelCommand.CanExecute(null));
        Assert.True(vm.PrevLabelCommand.CanExecute(null));
    }

    [Fact]
    public void SingleRecordMode_ShowsThisRecord()
    {
        var rows = new[] { Row("a", 1) };
        var vm = new PrintPreviewViewModel(new Core.Models.LabelTemplate(), rows[0].Fields,
            dataQty: 1, allRows: rows) { PrintAllRecords = false };
        Assert.Equal("This record", vm.PreviewInfo);
        Assert.False(vm.NextLabelCommand.CanExecute(null));
    }

    [Fact]
    public void AllRowsZeroQty_SaysNothingWillPrint()
    {
        var rows = new[] { Row("a", 0), Row("b", 0) };
        var vm = new PrintPreviewViewModel(new Core.Models.LabelTemplate(), rows[0].Fields,
            dataQty: 0, allRows: rows);
        Assert.Contains("Nothing to print", vm.PreviewInfo);
    }
}

public class DatabaseFieldSourceTests
{
    private static Core.Models.DataSourceDefinition DbField(string name, string sourceColumn) => new()
    {
        Name = name,
        Type = Core.Models.DataSourceType.DatabaseField,
        SourceField = sourceColumn
    };

    [Fact]
    public void DatabaseField_MirrorsTheMappedColumn()
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Item Description (EN)"] = "Mint 50ml"
        };
        Helpers.DataSourceResolver.ApplyDerived(new[] { DbField("ProductName", "Item Description (EN)") }, fields);
        Assert.Equal("Mint 50ml", fields["ProductName"]);
    }

    [Fact]
    public void RePointingTheSource_FollowsTheNewColumn()
    {
        // The whole point: when the data file changes, fix the mapping ONCE — elements
        // bound to "ProductName" follow without being touched.
        var ds = DbField("ProductName", "OldColumn");
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OldColumn"] = "from old", ["NewColumn"] = "from new"
        };
        Helpers.DataSourceResolver.ApplyDerived(new[] { ds }, fields);
        Assert.Equal("from old", fields["ProductName"]);

        ds.SourceField = "NewColumn";
        Helpers.DataSourceResolver.ApplyDerived(new[] { ds }, fields);
        Assert.Equal("from new", fields["ProductName"]);
    }

    [Fact]
    public void MissingColumn_ResolvesEmpty_NeverThrows()
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Helpers.DataSourceResolver.ApplyDerived(new[] { DbField("X", "NoSuchColumn") }, fields);
        Assert.Equal("", fields["X"]);
    }

    [Fact]
    public void Formulas_CanReferenceDatabaseFieldSources()
    {
        var sources = new Core.Models.DataSourceDefinition[]
        {
            DbField("Prod", "Col A"),
            new() { Name = "Label", Type = Core.Models.DataSourceType.Formula, FormulaExpression = "UPPER({Prod})" }
        };
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Col A"] = "mint" };
        Helpers.DataSourceResolver.ApplyDerived(sources, fields);
        Assert.Equal("MINT", fields["Label"]);
    }

    [Fact]
    public void StaticResolve_SkipsDatabaseFields()
    {
        // DatabaseField needs row data; the row-independent Resolve must not emit a bogus value.
        var resolved = Helpers.DataSourceResolver.Resolve(new[] { DbField("ProductName", "Col") });
        Assert.False(resolved.ContainsKey("ProductName"));
    }
}

public class PageLayoutTests
{
    [Fact]
    public void CellOrigin_AcrossFirst_WalksColumnsThenRows()
    {
        var p = new Core.Models.PageLayout
        {
            Rows = 2, Columns = 3, MarginLeftMm = 5, MarginTopMm = 10, GutterXMm = 2, GutterYMm = 4
        };
        // label 50 × 20 → pitch X = 52, pitch Y = 24
        Assert.Equal((5.0, 10.0),  p.CellOriginMm(0, 50, 20));
        Assert.Equal((57.0, 10.0), p.CellOriginMm(1, 50, 20));
        Assert.Equal((109.0, 10.0), p.CellOriginMm(2, 50, 20));
        Assert.Equal((5.0, 34.0),  p.CellOriginMm(3, 50, 20));   // wraps to row 2
    }

    [Fact]
    public void CellOrigin_DownFirst_WalksRowsThenColumns()
    {
        var p = new Core.Models.PageLayout { Rows = 2, Columns = 3, FillAcrossFirst = false };
        Assert.Equal((0.0, 0.0),  p.CellOriginMm(0, 10, 10));
        Assert.Equal((0.0, 10.0), p.CellOriginMm(1, 10, 10));    // down first
        Assert.Equal((10.0, 0.0), p.CellOriginMm(2, 10, 10));    // then next column
    }

    [Fact]
    public void CellOrigin_MirrorColumns_FlipsLeftRight_ForDuplexBacks()
    {
        var p = new Core.Models.PageLayout { Rows = 1, Columns = 3 };
        // Cell 0's back must land where cell 2 sits (long-edge flip), same row.
        Assert.Equal(p.CellOriginMm(2, 10, 10), p.CellOriginMm(0, 10, 10, mirrorColumns: true));
        Assert.Equal(p.CellOriginMm(1, 10, 10), p.CellOriginMm(1, 10, 10, mirrorColumns: true));
    }

    [Fact]
    public void Avery5160_Is30UpOnLetter_AndLastCellFits()
    {
        var p = Core.Models.PageLayout.Avery5160();
        Assert.Equal(30, p.CellsPerPage);
        var (x, y) = p.CellOriginMm(29, 66.7, 25.4);   // bottom-right label
        Assert.True(x + 66.7 <= p.PageWidthMm + 0.01, $"right edge {x + 66.7} exceeds page {p.PageWidthMm}");
        Assert.True(y + 25.4 <= p.PageHeightMm + 0.01, $"bottom edge {y + 25.4} exceeds page {p.PageHeightMm}");
    }

    [Fact]
    public void SameGridAs_ComparesGeometryOnly()
    {
        var a = Core.Models.PageLayout.Avery5160();
        var b = Core.Models.PageLayout.Avery5160();
        b.BackTemplateName = "Some Back";            // not part of the grid
        Assert.True(a.SameGridAs(b));
        b.GutterXMm += 1;
        Assert.False(a.SameGridAs(b));
        Assert.False(a.SameGridAs(null));
    }

    [Fact]
    public void PageLayout_SurvivesTemplateRoundTrip_AndOldTemplatesLoadWithNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ld-page-" + Guid.NewGuid().ToString("N"));
        try
        {
            var t = new Core.Models.LabelTemplate { Name = "Sheet", WidthMm = 66.7, HeightMm = 25.4 };
            t.Page = Core.Models.PageLayout.Avery5160();
            t.Page.BackTemplateName = "Backside";

            var svc = new Core.Services.TemplateService(dir);
            var path = Path.Combine(dir, "s.lbl");
            svc.Save(t, path);
            var loaded = svc.Load(path)!;

            Assert.NotNull(loaded.Page);
            Assert.Equal(30, loaded.Page!.CellsPerPage);
            Assert.Equal("Backside", loaded.Page.BackTemplateName);
            Assert.True(loaded.Page.SameGridAs(t.Page));

            // Templates saved before Page existed must load with Page == null (direct printing).
            var legacy = System.Text.Json.JsonSerializer.Deserialize<Core.Models.LabelTemplate>(
                """{ "Name": "Old", "WidthMm": 50.8, "HeightMm": 25.4 }""",
                Core.Services.TemplateService.JsonOptions)!;
            Assert.Null(legacy.Page);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}

public class SheetCompositionTests
{
    /// <summary>Runs WPF visual-tree code on an STA thread (xunit runs MTA).</summary>
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new System.Threading.Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }

    private static Core.Models.LabelTemplate BlackLabelOnAvery() => new()
    {
        Name = "SheetTest", WidthMm = 66.7, HeightMm = 25.4,
        Page = Core.Models.PageLayout.Avery5160(),
        Elements =
        {
            new Core.Models.ShapeElement
            {
                X = 0, Y = 0,
                Width = Core.Models.LabelTemplate.MmToPixels(66.7),
                Height = Core.Models.LabelTemplate.MmToPixels(25.4),
                FillColor = "#000000", StrokeColor = "#000000"
            }
        }
    };

    private static bool IsDarkAtMm(System.Windows.Media.Imaging.BitmapSource bmp, double xMm, double yMm)
    {
        int px = (int)(Core.Models.LabelTemplate.MmToPixels(xMm) * bmp.DpiX / 96.0);
        int py = (int)(Core.Models.LabelTemplate.MmToPixels(yMm) * bmp.DpiY / 96.0);
        var pixel = new byte[4];
        var crop = new System.Windows.Media.Imaging.CroppedBitmap(bmp, new System.Windows.Int32Rect(px, py, 1, 1));
        var fmt = new System.Windows.Media.Imaging.FormatConvertedBitmap(crop, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        fmt.CopyPixels(pixel, 4, 0);
        return pixel[0] < 100 && pixel[1] < 100 && pixel[2] < 100;
    }

    [Fact]
    public void RenderSheetPreview_PlacesLabelsInTheRightCells()
    {
        RunSta(() =>
        {
            var t = BlackLabelOnAvery();
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var labels = new (Core.Models.LabelTemplate, Dictionary<string, string>)[] { (t, fields), (t, fields) };

            var bmp = PrintService.RenderSheetPreview(t, labels, startCell: 0, dpi: 96);

            // Page is Letter-sized.
            Assert.Equal((int)Core.Models.LabelTemplate.MmToPixels(215.9), bmp.PixelWidth);

            var p = t.Page!;
            var c0 = p.CellOriginMm(0, 66.7, 25.4);
            var c1 = p.CellOriginMm(1, 66.7, 25.4);
            var c2 = p.CellOriginMm(2, 66.7, 25.4);
            Assert.True(IsDarkAtMm(bmp, c0.XMm + 33, c0.YMm + 12), "cell 0 should hold label 1");
            Assert.True(IsDarkAtMm(bmp, c1.XMm + 33, c1.YMm + 12), "cell 1 should hold label 2");
            Assert.False(IsDarkAtMm(bmp, c2.XMm + 33, c2.YMm + 12), "cell 2 should be empty");
        });
    }

    [Fact]
    public void RenderSheetPreview_HonoursStartCell()
    {
        RunSta(() =>
        {
            var t = BlackLabelOnAvery();
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var bmp = PrintService.RenderSheetPreview(t,
                new (Core.Models.LabelTemplate, Dictionary<string, string>)[] { (t, fields) },
                startCell: 4, dpi: 96);

            var p = t.Page!;
            var c0 = p.CellOriginMm(0, 66.7, 25.4);
            var c4 = p.CellOriginMm(4, 66.7, 25.4);   // row 2, middle column (across-first)
            Assert.False(IsDarkAtMm(bmp, c0.XMm + 33, c0.YMm + 12), "cell 0 must stay empty (part-used sheet)");
            Assert.True(IsDarkAtMm(bmp, c4.XMm + 33, c4.YMm + 12), "the label must land at cell 5");
        });
    }
}

public class TableFillsBoxTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new System.Threading.Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }

    private static bool IsDarkAt(System.Windows.Media.Imaging.BitmapSource bmp, double fracX, double fracY)
    {
        int px = (int)(bmp.PixelWidth * fracX), py = (int)(bmp.PixelHeight * fracY);
        var pixel = new byte[4];
        var crop = new System.Windows.Media.Imaging.CroppedBitmap(bmp, new System.Windows.Int32Rect(px, py, 1, 1));
        var fmt = new System.Windows.Media.Imaging.FormatConvertedBitmap(crop, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        fmt.CopyPixels(pixel, 4, 0);
        return pixel[0] < 100 && pixel[1] < 100 && pixel[2] < 100;
    }

    [Fact]
    public void Table_ScalesToFillElementBox_NotClippedAtNaturalSize()
    {
        RunSta(() =>
        {
            // A 1-column table on an 80×40 mm white label. With the old clip-at-natural-size
            // behaviour the bottom-right of the box would be blank; the container-bound layout
            // (TableLayout) fills the element box, so the black-celled table covers all of it.
            var t = new Core.Models.LabelTemplate { Name = "T", WidthMm = 80, HeightMm = 40, BackgroundColor = "#FFFFFF" };
            t.Elements.Add(new Core.Models.TableElement
            {
                X = 0, Y = 0,
                Width = Core.Models.LabelTemplate.MmToPixels(80),
                Height = Core.Models.LabelTemplate.MmToPixels(40),
                ShowHeader = false,
                CellBackground = "#000000",
                BorderColor = "#000000",
                Columns = { },
                StaticRows = { new System.Collections.Generic.List<string> { "x" } }
            });
            // one column
            ((Core.Models.TableElement)t.Elements[0]).Columns.Add(new Core.Models.TableColumn { Header = "C", Width = 60 });

            var bmp = PrintService.RenderPreview(t, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), dpi: 96);

            Assert.True(IsDarkAt(bmp, 0.5, 0.5),  "table should cover the centre of the box");
            Assert.True(IsDarkAt(bmp, 0.85, 0.85), "table should reach the bottom-right — it scaled to fill, not clipped");
        });
    }
}

public class TableRowEditingTests
{
    [Fact]
    public void AddColumn_ExtendsExistingRowsCells()
    {
        var vm = new TableElementViewModel();          // 1 column, 1 seeded row
        vm.Rows[0].Cells[0].Value = "keep me";

        vm.AddColumnCommand.Execute(null);
        Assert.Equal(2, vm.Columns.Count);
        Assert.All(vm.Rows, r => Assert.Equal(2, r.Cells.Count));
        Assert.Equal("keep me", vm.Rows[0].Cells[0].Value);

        vm.RemoveColumnCommand.Execute(null);
        Assert.All(vm.Rows, r => Assert.Single(r.Cells));
    }

    [Fact]
    public void RowsRoundTrip_ThroughModelStaticRows()
    {
        var vm = new TableElementViewModel();
        vm.Rows[0].Cells[0].Value = "A1";
        vm.AddRowCommand.Execute(null);
        vm.Rows[1].Cells[0].Value = "A2";

        var model = (Core.Models.TableElement)vm.ToModel();
        Assert.Equal(2, model.StaticRows.Count);
        Assert.Equal("A1", model.StaticRows[0][0]);
        Assert.Equal("A2", model.StaticRows[1][0]);

        var vm2 = new TableElementViewModel();
        vm2.FromModel(model);
        Assert.Equal(2, vm2.Rows.Count);
        Assert.Equal("A2", vm2.Rows[1].Cells[0].Value);
    }
}
