using LabelDesigner.Services;
using LabelDesigner.ViewModels;
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
