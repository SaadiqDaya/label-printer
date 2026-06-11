using LabelDesigner.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LabelDesigner.Views;

/// <summary>
/// Spreadsheet-style editor for a table element's row data, opened by double-clicking the table on
/// the canvas. One DataGrid column per table column; cells bind by index into a string[] per row,
/// so any header text is safe. OK writes the rows back to the TableElementViewModel.
/// </summary>
public partial class TableEditDialog : Window
{
    private readonly TableElementViewModel _vm;
    private readonly ObservableCollection<string[]> _rows = new();
    private readonly int _colCount;

    public TableEditDialog(TableElementViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _colCount = vm.Columns.Count;

        for (int c = 0; c < _colCount; c++)
        {
            var header = string.IsNullOrWhiteSpace(vm.Columns[c].Header) ? $"Column {c + 1}" : vm.Columns[c].Header;
            EditGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = new TextBlock { Text = header },   // TextBlock so underscores aren't access keys
                Binding = new Binding($"[{c}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
                Width   = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        foreach (var row in vm.Rows)
        {
            var arr = new string[_colCount];
            for (int c = 0; c < _colCount; c++)
                arr[c] = c < row.Cells.Count ? row.Cells[c].Value : "";
            _rows.Add(arr);
        }
        if (_rows.Count == 0) _rows.Add(new string[_colCount]);   // always at least one row to type into

        EditGrid.ItemsSource = _rows;
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        var arr = new string[_colCount];
        Array.Fill(arr, "");
        _rows.Add(arr);
    }

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0) return;
        var idx = EditGrid.SelectedIndex >= 0 && EditGrid.SelectedIndex < _rows.Count
            ? EditGrid.SelectedIndex
            : _rows.Count - 1;
        _rows.RemoveAt(idx);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // Commit a cell that's still in edit mode before reading the rows back.
        EditGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        EditGrid.CommitEdit(DataGridEditingUnit.Row, true);

        _vm.Rows.Clear();
        foreach (var arr in _rows)
            _vm.Rows.Add(new TableRowViewModel(arr.Select(v => v ?? ""), _colCount));

        DialogResult = true;
    }
}
