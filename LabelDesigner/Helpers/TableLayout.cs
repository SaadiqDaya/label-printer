using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LabelDesigner.Helpers;

/// <summary>
/// The ONE table layout used by both the designer canvas and the print path, so they cannot
/// drift apart (render parity). The table is laid out AT the element box's size: column widths
/// are star proportions, all rows (header included) share the height equally, and fonts stay at
/// their configured sizes — resizing the box gives the cells more room instead of scaling or
/// clipping the content (the old Viewbox-Fill approach distorted text and truncated headers).
/// </summary>
public static class TableLayout
{
    public static FrameworkElement Build(
        IReadOnlyList<(string Header, double Width)> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool showHeader,
        Brush? headerFill, Brush? cellFill, Brush? borderBrush, double borderThickness,
        string fontFamily, double headerFontSize, double cellFontSize)
    {
        var outer = new Border
        {
            Background      = cellFill,
            BorderBrush     = borderBrush,
            BorderThickness = new Thickness(Math.Max(0, borderThickness)),
            ClipToBounds    = true
        };
        if (columns.Count == 0) return outer;

        var font = SafeFont(fontFamily);
        var grid = new Grid();
        outer.Child = grid;

        foreach (var col in columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(Math.Max(0.01, col.Width), GridUnitType.Star) });

        int headerRows = showHeader ? 1 : 0;
        int totalRows  = headerRows + Math.Max(1, rows.Count);
        for (int r = 0; r < totalRows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Interior gridlines only — the outer Border owns the outside edge.
        double bt = Math.Max(0, borderThickness);
        Thickness CellEdges(int r, int c) =>
            new(0, 0, c < columns.Count - 1 ? bt : 0, r < totalRows - 1 ? bt : 0);

        if (showHeader)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                var cell = new Border
                {
                    Background      = headerFill,
                    BorderBrush     = borderBrush,
                    BorderThickness = CellEdges(0, c),
                    ClipToBounds    = true,
                    Child = new TextBlock
                    {
                        Text                = columns[c].Header,
                        FontFamily          = font,
                        FontSize            = headerFontSize,
                        FontWeight          = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                        TextTrimming        = TextTrimming.CharacterEllipsis,
                        Margin              = new Thickness(2, 0, 2, 0)
                    }
                };
                Grid.SetRow(cell, 0);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                var cell = new Border
                {
                    BorderBrush     = borderBrush,
                    BorderThickness = CellEdges(headerRows + r, c),
                    ClipToBounds    = true,
                    Child = new TextBlock
                    {
                        Text                = c < rows[r].Count ? rows[r][c] : "",
                        FontFamily          = font,
                        FontSize            = cellFontSize,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                        TextWrapping        = TextWrapping.Wrap,
                        TextTrimming        = TextTrimming.CharacterEllipsis,
                        Margin              = new Thickness(2, 0, 2, 0)
                    }
                };
                Grid.SetRow(cell, headerRows + r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        return outer;
    }

    /// <summary>Explicit family always — a table must never inherit the app's UI font (parity).</summary>
    private static FontFamily SafeFont(string family)
    {
        try { return new FontFamily(string.IsNullOrWhiteSpace(family) ? "Arial" : family); }
        catch { return new FontFamily("Arial"); }
    }
}
