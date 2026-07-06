using LabelDesigner.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LabelDesigner.Views;

/// <summary>
/// Spreadsheet-style editor for a table element's data, opened by double-clicking the table on the
/// canvas. One DataGrid column per table column; cells bind by index into a string[] per row, so any
/// header text is safe. Rows AND columns can be added/removed; OK writes everything back to the
/// TableElementViewModel (new columns get default headers — rename them in the Properties panel).
/// </summary>
public partial class TableEditDialog : Window
{
    private readonly TableElementViewModel _vm;
    private readonly ObservableCollection<string[]> _rows = new();
    private readonly List<TextBox> _headerBoxes = new();
    private int _colCount;

    public TableEditDialog(TableElementViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _colCount = vm.Columns.Count;

        for (int c = 0; c < _colCount; c++)
            EditGrid.Columns.Add(MakeColumn(vm.Columns[c].Header, c));

        foreach (var row in vm.Rows)
        {
            var arr = new string[_colCount];
            for (int c = 0; c < _colCount; c++)
                arr[c] = c < row.Cells.Count ? row.Cells[c].Value : "";
            _rows.Add(arr);
        }
        if (_rows.Count == 0) _rows.Add(NewRow());   // always at least one row to type into

        EditGrid.ItemsSource = _rows;
    }

    private DataGridTextColumn MakeColumn(string header, int index)
    {
        // The column header IS a TextBox — headers are edited in place, right above their column.
        var headerBox = new TextBox
        {
            Text     = string.IsNullOrWhiteSpace(header) ? $"Column {index + 1}" : header,
            MinWidth = 60,
            ToolTip  = "Column header — click to rename"
        };
        _headerBoxes.Add(headerBox);
        return new DataGridTextColumn
        {
            Header  = headerBox,
            Binding = new Binding($"[{index}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width   = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
    }

    private string[] NewRow()
    {
        var arr = new string[_colCount];
        Array.Fill(arr, "");
        return arr;
    }

    private void CommitPendingEdits()
    {
        EditGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        EditGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        _rows.Add(NewRow());
    }

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0) return;
        CommitPendingEdits();
        var idx = EditGrid.SelectedIndex >= 0 && EditGrid.SelectedIndex < _rows.Count
            ? EditGrid.SelectedIndex
            : _rows.Count - 1;
        _rows.RemoveAt(idx);
    }

    private void AddColumn_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        _colCount++;
        EditGrid.Columns.Add(MakeColumn($"Column {_colCount}", _colCount - 1));
        ResizeAllRows();
    }

    private void RemoveColumn_Click(object sender, RoutedEventArgs e)
    {
        if (_colCount <= 1) return;   // a table always keeps at least one column
        CommitPendingEdits();
        _colCount--;
        EditGrid.Columns.RemoveAt(EditGrid.Columns.Count - 1);
        _headerBoxes.RemoveAt(_headerBoxes.Count - 1);
        ResizeAllRows();
    }

    /// <summary>Re-shapes every row's value array to the current column count (replacing the item
    /// raises a collection Replace, which refreshes the DataGrid's cell bindings).</summary>
    private void ResizeAllRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var resized = NewRow();
            var old = _rows[i];
            for (int c = 0; c < Math.Min(old.Length, resized.Length); c++)
                resized[c] = old[c] ?? "";
            _rows[i] = resized;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();

        // Sync the column COUNT through the VM's own commands (they keep canvas rows in step).
        // Bound fields/widths of surviving columns are untouched; new ones get defaults.
        while (_vm.Columns.Count < _colCount) _vm.AddColumnCommand.Execute(null);
        while (_vm.Columns.Count > _colCount && _vm.Columns.Count > 1) _vm.RemoveColumnCommand.Execute(null);

        // Header names come straight from the in-place header boxes.
        for (int c = 0; c < _colCount && c < _headerBoxes.Count; c++)
            _vm.Columns[c].Header = _headerBoxes[c].Text.Trim();

        _vm.Rows.Clear();
        foreach (var arr in _rows)
            _vm.Rows.Add(new TableRowViewModel(arr.Select(v => v ?? ""), _colCount));

        DialogResult = true;
    }
}
