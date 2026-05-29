using LabelDesigner.Services;
using System.ComponentModel;
using System.Data;
using System.Windows;

namespace LabelDesigner.Views;

/// <summary>
/// Simple grid view of all loaded records. Double-click jumps to that row in the designer.
/// </summary>
public partial class RecordBrowserDialog : Window
{
    /// <summary>Index of the row the user picked (0-based), or -1 if cancelled.</summary>
    public int SelectedIndex { get; private set; } = -1;

    public RecordBrowserDialog(IReadOnlyList<ExcelRow> rows, int initialIndex = -1)
    {
        InitializeComponent();

        // Build a DataTable so DataGrid can auto-generate columns from the first row's fields.
        var table = new DataTable();
        table.Columns.Add("#", typeof(int));
        table.Columns.Add("Qty", typeof(int));

        // Collect the union of all field names (preserve first-seen order).
        var fieldOrder = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
            foreach (var k in r.Fields.Keys)
                if (seen.Add(k)) fieldOrder.Add(k);

        foreach (var name in fieldOrder)
            table.Columns.Add(name, typeof(string));

        for (int i = 0; i < rows.Count; i++)
        {
            var values = new object?[2 + fieldOrder.Count];
            values[0] = i + 1;
            values[1] = rows[i].PrintQty;
            for (int c = 0; c < fieldOrder.Count; c++)
                values[2 + c] = rows[i].Fields.TryGetValue(fieldOrder[c], out var v) ? v : "";
            table.Rows.Add(values);
        }

        RecordsGrid.ItemsSource = table.DefaultView;

        if (initialIndex >= 0 && initialIndex < rows.Count)
        {
            RecordsGrid.SelectedIndex = initialIndex;
            RecordsGrid.ScrollIntoView(RecordsGrid.SelectedItem);
        }
    }

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RecordsGrid.SelectedIndex < 0) return;
        SelectedIndex = RecordsGrid.SelectedIndex;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
