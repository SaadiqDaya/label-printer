using LabelDesigner.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace LabelDesigner.Services;

/// <summary>A 1-bpp image packed for a ZPL ^GFA field (black bit = printed dot).</summary>
public readonly record struct ZplBitmap(byte[] Data, int WidthDots, int HeightDots, int BytesPerRow);

/// <summary>
/// Renders a label to native ZPL II for Zebra printers. Text, barcodes, boxes/lines/ellipses are
/// emitted as native ZPL (printer-engine barcodes — no rasterisation, perfectly scannable). Images
/// and non-rectangular shapes fall back to a ^GFA monochrome raster via the supplied rasterizer.
/// Mirrors PrintService's element walk + condition/layer gating for output parity.
///
/// NOTE: opt-in (PrinterProfile.OutputMode = Zpl). MUST be validated on physical Zebra hardware —
/// barcode command parameters and field orientation are best-effort until proven on the ZD621.
/// </summary>
public static class ZplRenderer
{
    public static string Render(LabelTemplate template, Dictionary<string, string> fields,
        Func<LabelElement, Dictionary<string, string>, ZplBitmap?>? rasterize = null)
    {
        int dpi = template.Dpi;
        int D(double px) => (int)Math.Round(px * dpi / 96.0);

        var prof = template.PrinterProfile;
        var sb = new StringBuilder();
        sb.Append("^XA^CI28");
        sb.Append($"^PW{template.WidthDots}");
        sb.Append($"^LL{template.HeightDots}");
        sb.Append($"^LH{D(LabelTemplate.MmToPixels(prof.LabelOffsetXMm))},{D(LabelTemplate.MmToPixels(prof.LabelOffsetYMm))}");
        if (prof.Darkness is int dk) sb.Append($"^MD{Math.Clamp(dk, 0, 30)}");
        if (prof.SpeedIps is double sp) sb.Append($"^PR{Math.Clamp((int)Math.Round(sp), 1, 14)}");
        sb.Append(prof.MediaType switch
        {
            ThermalMediaType.Continuous => "^MNN",
            ThermalMediaType.BlackMark  => "^MNM",
            _                           => "^MNY"
        });

        // Painter's-order walk identical to BuildPrintVisual.
        int layerCount = template.Layers.Count;
        var layerIndex = template.Layers.Select((l, i) => (l.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var sorted = template.Elements.OrderBy(e =>
        {
            int lb = e.LayerId.HasValue && layerIndex.TryGetValue(e.LayerId.Value, out var li) ? (layerCount - li) * 1000 : 0;
            return lb + e.ZIndex;
        });

        foreach (var el in sorted)
        {
            if (!ConditionEvaluator.Evaluate(el.PrintCondition, fields)) continue;
            if (el.LayerId.HasValue)
            {
                var layer = template.Layers.FirstOrDefault(l => l.Id == el.LayerId);
                if (layer != null && !string.IsNullOrWhiteSpace(layer.PrintCondition) &&
                    !ConditionEvaluator.Evaluate(layer.PrintCondition, fields))
                    continue;
            }

            int x = D(el.X), y = D(el.Y);
            string orient = Orient(el.Rotation);
            switch (el)
            {
                case TextElement te: AppendText(sb, te, fields, x, y, D, orient); break;
                case BarcodeElement be: AppendBarcode(sb, be, fields, x, y, D, orient, dpi); break;
                case ShapeElement se when se.ShapeType is ShapeType.Rectangle or ShapeType.Ellipse or ShapeType.Line:
                    AppendShape(sb, se, x, y, D); break;
                default: AppendRaster(sb, el, fields, x, y, rasterize); break;
            }
        }

        sb.Append("^XZ");
        return sb.ToString();
    }

    private static string Orient(double rot) => ((((int)Math.Round(rot / 90.0)) % 4 + 4) % 4) switch
    {
        1 => "R", 2 => "I", 3 => "B", _ => "N"
    };

    /// <summary>Strips ZPL control chars from data so a field value can't break the command stream.</summary>
    private static string Esc(string s) => s.Replace("^", " ").Replace("~", " ");

    private static void AppendText(StringBuilder sb, TextElement te, Dictionary<string, string> fields,
        int x, int y, Func<double, int> D, string orient)
    {
        string val = te.BoundField != null && fields.TryGetValue(te.BoundField, out var fv) ? fv : Substitute(te.Text, fields);
        int h = Math.Max(6, D(te.FitToBox ? te.Height : te.FontSize));
        sb.Append($"^FO{x},{y}^A0{orient},{h},0");
        if (te.MultiLine)
        {
            string al = te.Alignment switch { TextAlignmentOption.Center => "C", TextAlignmentOption.Right => "R", _ => "L" };
            sb.Append($"^FB{D(te.Width)},20,0,{al}");
        }
        sb.Append($"^FD{Esc(val)}^FS");
    }

    private static void AppendBarcode(StringBuilder sb, BarcodeElement be, Dictionary<string, string> fields,
        int x, int y, Func<double, int> D, string orient, int dpi)
    {
        string val = be.BoundField != null && fields.TryGetValue(be.BoundField, out var fv) ? fv : be.BarcodeValue;
        int h = Math.Max(10, D(be.Height));
        int mod = be.XDimensionMm > 0 ? Math.Clamp((int)Math.Round(be.XDimensionMm * dpi / 25.4), 1, 10) : 2;
        string interp = be.ShowText ? "Y" : "N";
        sb.Append($"^FO{x},{y}^BY{mod}");

        switch (be.Format)
        {
            case BarcodeFormatOption.GS1_128:
                sb.Append($"^BC{orient},{h},{interp},N,N^FD>;{Esc(val.Replace("(", "").Replace(")", ""))}^FS"); break;
            case BarcodeFormatOption.Code39:  sb.Append($"^B3{orient},N,{h},{interp},N^FD{Esc(val)}^FS"); break;
            case BarcodeFormatOption.Code93:  sb.Append($"^BA{orient},{h},{interp},N^FD{Esc(val)}^FS"); break;
            case BarcodeFormatOption.ITF:     sb.Append($"^B2{orient},{h},{interp},N,N^FD{Esc(val)}^FS"); break;
            case BarcodeFormatOption.Codabar: sb.Append($"^BK{orient},N,{h},{interp},N,A,A^FD{Esc(val)}^FS"); break;
            case BarcodeFormatOption.EAN13:   sb.Append($"^BE{orient},{h},{interp},N^FD{Esc(val)}^FS"); break;
            case BarcodeFormatOption.UPCA:    sb.Append($"^BU{orient},{h},{interp},N^FD{Esc(val)}^FS"); break;
            case BarcodeFormatOption.QRCode:  sb.Append($"^BQ{orient},2,{mod}^FDQA,{Esc(val)}^FS"); break;
            case BarcodeFormatOption.DataMatrix: sb.Append($"^BX{orient},{Math.Clamp(mod * 3, 3, 30)},200^FD{Esc(val)}^FS"); break;
            case BarcodeFormatOption.PDF417:  sb.Append($"^B7{orient},{mod},5,0,0,N^FD{Esc(val)}^FS"); break;
            case BarcodeFormatOption.Aztec:   sb.Append($"^BO{orient},{mod},N,0,N,N^FD{Esc(val)}^FS"); break;
            default:                          sb.Append($"^BC{orient},{h},{interp},N,N^FD{Esc(val)}^FS"); break; // Code128
        }
    }

    private static void AppendShape(StringBuilder sb, ShapeElement se, int x, int y, Func<double, int> D)
    {
        int w = D(se.Width), h = D(se.Height), t = Math.Max(1, D(se.StrokeThickness));
        sb.Append($"^FO{x},{y}");
        if (se.ShapeType == ShapeType.Ellipse)
            sb.Append($"^GE{w},{h},{t},B^FS");
        else if (se.ShapeType == ShapeType.Line)
            sb.Append($"^GD{Math.Max(w, 1)},{Math.Max(h, 1)},{t},B,{(se.LineReverseY ? "L" : "R")}^FS");
        else
            sb.Append($"^GB{w},{h},{t},B,{Math.Clamp((int)Math.Round(se.CornerRadius / 3), 0, 8)}^FS");
    }

    private static void AppendRaster(StringBuilder sb, LabelElement el, Dictionary<string, string> fields,
        int x, int y, Func<LabelElement, Dictionary<string, string>, ZplBitmap?>? rasterize)
    {
        if (rasterize == null) return;
        if (rasterize(el, fields) is not ZplBitmap b || b.Data.Length == 0) return;
        var hex = new StringBuilder(b.Data.Length * 2);
        foreach (var by in b.Data) hex.Append(by.ToString("X2"));
        sb.Append($"^FO{x},{y}^GFA,{b.Data.Length},{b.Data.Length},{b.BytesPerRow},{hex}^FS");
    }

    private static string Substitute(string text, Dictionary<string, string> fields) =>
        Regex.Replace(text, @"\{\{(\w+)\}\}", m => fields.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
}
