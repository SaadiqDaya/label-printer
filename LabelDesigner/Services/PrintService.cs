using LabelDesigner.Core.Models;
using LabelDesigner.Helpers;
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
    /// <summary>
    /// Renders the template with field values and sends to printer.
    /// If printerName is null, uses the system default printer.
    /// Prints <paramref name="copies"/> copies (loops the print call).
    /// </summary>
    public static void Print(LabelTemplate template, Dictionary<string, string> fields,
        string? printerName = null, int copies = 1)
    {
        PrintQueue queue = GetPrintQueue(printerName);
        PrintTicket ticket = queue.DefaultPrintTicket;

        ticket.PageMediaSize = new PageMediaSize(
            PageMediaSizeName.Unknown,
            template.WidthPx,
            template.HeightPx);

        var dialog = new PrintDialog { PrintQueue = queue, PrintTicket = ticket };
        copies = Math.Max(1, copies);
        for (int i = 0; i < copies; i++)
        {
            var visual = BuildPrintVisual(template, fields);
            dialog.PrintVisual(visual, template.Name);
        }
    }

    /// <summary>Renders the template to a BitmapSource (for preview).</summary>
    public static BitmapSource RenderPreview(LabelTemplate template, Dictionary<string, string> fields, double dpi = 96)
    {
        var visual = BuildPrintVisual(template, fields);
        visual.Measure(new Size(template.WidthPx, template.HeightPx));
        visual.Arrange(new Rect(0, 0, template.WidthPx, template.HeightPx));
        visual.UpdateLayout();
        return BitmapHelper.RenderVisual(visual, template.WidthPx, template.HeightPx, dpi);
    }

    // ─── Build print visual ────────────────────────────────────────────────
    private static UIElement BuildPrintVisual(LabelTemplate template, Dictionary<string, string> fields)
    {
        var canvas = new Canvas
        {
            Width = template.WidthPx,
            Height = template.HeightPx,
            Background = ParseBrush(template.BackgroundColor)
        };

        foreach (var element in template.Elements.OrderBy(e => e.ZIndex))
        {
            UIElement? ui = element switch
            {
                TextElement te => BuildText(te, fields),
                BarcodeElement be => BuildBarcode(be, fields),
                ImageElement ie => BuildImage(ie),
                ShapeElement se => BuildShape(se),
                _ => null
            };

            if (ui == null) continue;

            Canvas.SetLeft(ui, element.X);
            Canvas.SetTop(ui, element.Y);
            if (ui is FrameworkElement fe) { fe.Width = element.Width; fe.Height = element.Height; }
            Panel.SetZIndex(ui, element.ZIndex);
            canvas.Children.Add(ui);
        }

        return canvas;
    }

    /// <summary>Replaces {{fieldName}} tokens in text using the fields dictionary.</summary>
    private static string Substitute(string text, Dictionary<string, string> fields) =>
        Regex.Replace(text, @"\{\{(\w+)\}\}", m =>
            fields.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    private static TextBlock BuildText(TextElement te, Dictionary<string, string> fields)
    {
        string value;
        if (te.BoundField != null && fields.TryGetValue(te.BoundField, out var fv))
            value = fv;
        else
            value = Substitute(te.Text, fields);
        var tb = new TextBlock
        {
            Text = value,
            FontFamily = new FontFamily(te.FontFamily),
            FontSize = te.FontSize,
            FontWeight = te.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = te.Italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = ParseBrush(te.Color),
            TextAlignment = te.Alignment switch
            {
                TextAlignmentOption.Center => TextAlignment.Center,
                TextAlignmentOption.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            },
            TextWrapping = TextWrapping.Wrap
        };
        if (te.Underline) tb.TextDecorations = TextDecorations.Underline;
        return tb;
    }

    private static UIElement? BuildBarcode(BarcodeElement be, Dictionary<string, string> fields)
    {
        var value = be.BoundField != null && fields.TryGetValue(be.BoundField, out var fv) ? fv : be.BarcodeValue;
        var img = BitmapHelper.GenerateBarcode(value, be.Format, (int)be.Width, (int)be.Height);
        if (img == null) return null;
        return new Image { Source = img, Stretch = Stretch.Fill };
    }

    private static UIElement? BuildImage(ImageElement ie)
    {
        if (!File.Exists(ie.ImagePath)) return null;
        try
        {
            var src = new BitmapImage(new Uri(ie.ImagePath));
            return new Image
            {
                Source = src,
                Stretch = ie.MaintainAspectRatio ? Stretch.Uniform : Stretch.Fill,
                Opacity = ie.Opacity
            };
        }
        catch { return null; }
    }

    private static UIElement BuildShape(ShapeElement se)
    {
        var fill = ParseBrush(se.FillColor);
        var stroke = ParseBrush(se.StrokeColor);

        return se.ShapeType switch
        {
            ShapeType.Ellipse => new System.Windows.Shapes.Ellipse
                { Fill = fill, Stroke = stroke, StrokeThickness = se.StrokeThickness },
            ShapeType.Line => new System.Windows.Shapes.Line
                { X1 = 0, Y1 = 0, X2 = se.Width, Y2 = se.Height, Stroke = stroke, StrokeThickness = se.StrokeThickness },
            _ => new System.Windows.Shapes.Rectangle
            {
                Fill = fill, Stroke = stroke, StrokeThickness = se.StrokeThickness,
                RadiusX = se.CornerRadius, RadiusY = se.CornerRadius
            }
        };
    }

    private static Brush ParseBrush(string color)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(color)!; }
        catch { return Brushes.Transparent; }
    }

    private static PrintQueue GetPrintQueue(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return LocalPrintServer.GetDefaultPrintQueue();

        using var server = new LocalPrintServer();
        foreach (PrintQueue q in server.GetPrintQueues())
            if (q.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return q;

        return LocalPrintServer.GetDefaultPrintQueue();
    }
}
