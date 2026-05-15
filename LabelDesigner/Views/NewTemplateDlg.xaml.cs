using System.Windows;
using System.Windows.Input;

namespace LabelDesigner.Views;

public partial class NewTemplateDlg : Window
{
    public double WidthMm { get; private set; } = 50.8;
    public double HeightMm { get; private set; } = 25.4;
    public string TemplateName { get; private set; } = "New Template";
    public List<string> Fields { get; private set; } = new();

    public NewTemplateDlg() => InitializeComponent();

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(WidthBox.Text, out var w) || w <= 0) { MessageBox.Show("Invalid width."); return; }
        if (!double.TryParse(HeightBox.Text, out var h) || h <= 0) { MessageBox.Show("Invalid height."); return; }
        WidthMm = w;
        HeightMm = h;
        TemplateName = string.IsNullOrWhiteSpace(NameBox.Text) ? "New Template" : NameBox.Text.Trim();
        Fields = FieldsList.Items.Cast<string>().ToList();
        DialogResult = true;
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((FrameworkElement)sender).Tag.ToString()!.Split(',');
        WidthBox.Text = parts[0];
        HeightBox.Text = parts[1];
    }

    private void AddField_Click(object sender, RoutedEventArgs e) => TryAddField();

    private void NewFieldBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryAddField();
    }

    private void TryAddField()
    {
        var name = NewFieldBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        // sanitise: letters/digits/underscore only
        name = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrEmpty(name)) return;
        if (!FieldsList.Items.Contains(name))
            FieldsList.Items.Add(name);
        NewFieldBox.Clear();
        NewFieldBox.Focus();
    }

    private void RemoveField_Click(object sender, RoutedEventArgs e)
    {
        if (FieldsList.SelectedItem != null)
            FieldsList.Items.Remove(FieldsList.SelectedItem);
    }
}
