using LabelDesigner.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LabelDesigner.Views;

/// <summary>
/// Simple grid view of all loaded records. Double-click jumps to that row in the designer.
/// Columns are built EXPLICITLY with indexer bindings ("[n]") into a string array per row —
/// auto-generating from a DataTable broke on real-world Excel headers: names containing
/// . / ( ) [ ] are parsed as binding-path syntax (blank columns), and a header named "#"/"Qty"
/// or a duplicate threw outright.
/// </summary>
public partial class RecordBrowserDialog : Window
{
    /// <summary>Index of the row the user picked (0-based), or -1 if cancelled.</summary>
    public int SelectedIndex { get; private set; } = -1;

    public RecordBrowserDialog(IReadOnlyList<ExcelRow> rows, int initialIndex = -1)
    {
        InitializeComponent();

        // Collect the union of all field names (preserve first-seen order).
        var fieldOrder = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
            foreach (var k in r.Fields.Keys)
                if (seen.Add(k)) fieldOrder.Add(k);

        RecordsGrid.Columns.Add(MakeColumn("#", 0));
        RecordsGrid.Columns.Add(MakeColumn("Qty", 1));
        for (int c = 0; c < fieldOrder.Count; c++)
            RecordsGrid.Columns.Add(MakeColumn(fieldOrder[c], 2 + c));

        var items = new List<string[]>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var values = new string[2 + fieldOrder.Count];
            values[0] = (i + 1).ToString();
            values[1] = rows[i].PrintQty.ToString();
            for (int c = 0; c < fieldOrder.Count; c++)
                values[2 + c] = rows[i].Fields.TryGetValue(fieldOrder[c], out var v) ? v : "";
            items.Add(values);
        }
        RecordsGrid.ItemsSource = items;

        if (initialIndex >= 0 && initialIndex < rows.Count)
        {
            RecordsGrid.SelectedIndex = initialIndex;
            RecordsGrid.ScrollIntoView(RecordsGrid.SelectedItem);
        }
    }

    private static DataGridTextColumn MakeColumn(string header, int index) => new()
    {
        // TextBlock header so underscores aren't eaten as access keys.
        Header  = new TextBlock { Text = header },
        Binding = new Binding($"[{index}]") { Mode = BindingMode.OneWay }
    };

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
