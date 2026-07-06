using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LabelDesigner.Helpers;
using Xunit;

namespace LabelDesigner.Tests;

/// <summary>
/// Structural tests for the shared table layout (canvas AND print use it — parity by
/// construction). Object-graph assertions only: no RenderTargetBitmap, so these stay
/// meaningful even when the machine's WPF raster pipeline is unavailable.
/// </summary>
public class TableLayoutTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }

    private static FrameworkElement Build(bool showHeader = true, int dataRows = 2) =>
        TableLayout.Build(
            new[] { ("Name", 60.0), ("Qty", 30.0) },
            Enumerable.Range(0, dataRows)
                .Select(i => (IReadOnlyList<string>)new[] { $"row{i}", $"{i}" }.ToList())
                .ToList(),
            showHeader,
            Brushes.LightGray, Brushes.White, Brushes.Black, 1,
            "Arial", 9, 8);

    [Fact]
    public void ColumnsAreStarProportions_SoTheTableStretchesToItsBox()
    {
        RunSta(() =>
        {
            var grid = (Grid)((Border)Build()).Child;
            Assert.Equal(2, grid.ColumnDefinitions.Count);
            Assert.All(grid.ColumnDefinitions, cd => Assert.True(cd.Width.IsStar,
                "column widths must be star units — absolute widths brought back the old clipping"));
            Assert.Equal(2.0, grid.ColumnDefinitions[0].Width.Value / grid.ColumnDefinitions[1].Width.Value, 3);
        });
    }

    [Fact]
    public void RowsShareTheHeightEqually_HeaderIncluded()
    {
        RunSta(() =>
        {
            var grid = (Grid)((Border)Build(showHeader: true, dataRows: 3)).Child;
            Assert.Equal(4, grid.RowDefinitions.Count);   // header + 3
            Assert.All(grid.RowDefinitions, rd => Assert.True(rd.Height.IsStar));

            var noHeader = (Grid)((Border)Build(showHeader: false, dataRows: 3)).Child;
            Assert.Equal(3, noHeader.RowDefinitions.Count);
        });
    }

    [Fact]
    public void FontsStayAtConfiguredSizes_NeverInheritTheAppFont()
    {
        RunSta(() =>
        {
            var grid = (Grid)((Border)Build()).Child;
            var blocks = grid.Children.OfType<Border>().Select(b => (TextBlock)b.Child).ToList();
            // Header cells bold at header size; data cells at cell size — no Viewbox scaling.
            Assert.Contains(blocks, tb => tb.FontSize == 9 && tb.FontWeight == FontWeights.Bold);
            Assert.Contains(blocks, tb => tb.FontSize == 8);
            Assert.All(blocks, tb => Assert.Equal("Arial", tb.FontFamily.Source));
        });
    }

    [Fact]
    public void OuterBorderOwnsTheEdge_InteriorCellsOnlyDrawRightAndBottom()
    {
        RunSta(() =>
        {
            var outer = (Border)Build(showHeader: true, dataRows: 1);
            Assert.Equal(new Thickness(1), outer.BorderThickness);

            var grid = (Grid)outer.Child;
            foreach (Border cell in grid.Children)
            {
                Assert.Equal(0, cell.BorderThickness.Left);
                Assert.Equal(0, cell.BorderThickness.Top);
            }
            // Bottom-right cell draws no interior lines at all (outer border covers it).
            var last = grid.Children.OfType<Border>()
                .First(b => Grid.GetRow(b) == 1 && Grid.GetColumn(b) == 1);
            Assert.Equal(new Thickness(0), last.BorderThickness);
        });
    }

    [Fact]
    public void EmptyColumns_ProduceAnEmptyBorderNotACrash()
    {
        RunSta(() =>
        {
            var fe = TableLayout.Build(Array.Empty<(string, double)>(),
                Array.Empty<IReadOnlyList<string>>(), true,
                Brushes.LightGray, Brushes.White, Brushes.Black, 1, "Arial", 9, 8);
            Assert.IsType<Border>(fe);
        });
    }
}
