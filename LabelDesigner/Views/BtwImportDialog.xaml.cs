using LabelDesigner.Services;
using System.IO;
using System.Windows;

namespace LabelDesigner.Views;

public partial class BtwImportDialog : Window
{
    public string  TemplateName { get; private set; } = "";
    public double  WidthMm      { get; private set; }
    public double  HeightMm     { get; private set; }

    public BtwImportDialog(string filePath, BtwMetadata? meta)
    {
        InitializeComponent();

        FileNameBlock.Text = filePath;

        if (meta != null)
        {
            SizeBlock.Text    = $"{meta.WidthMm:F1} × {meta.HeightMm:F1} mm";
            TitleBlock.Text   = meta.Title;
            PrinterBlock.Text = string.IsNullOrWhiteSpace(meta.Printer) ? "(not specified)" : meta.Printer;
            NameBox.Text      = meta.Title;
            WidthBox.Text     = meta.WidthMm.ToString("F1");
            HeightBox.Text    = meta.HeightMm.ToString("F1");
        }
        else
        {
            // Header couldn't be parsed — still let the user set up manually
            SizeBlock.Text    = "Could not read";
            TitleBlock.Text   = Path.GetFileNameWithoutExtension(filePath);
            PrinterBlock.Text = "(not specified)";
            NameBox.Text      = Path.GetFileNameWithoutExtension(filePath);
            WidthBox.Text     = "50.8";
            HeightBox.Text    = "25.4";
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(WidthBox.Text, out var w) || w <= 0)
        {
            MessageBox.Show("Enter a valid width.", "Invalid input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(HeightBox.Text, out var h) || h <= 0)
        {
            MessageBox.Show("Enter a valid height.", "Invalid input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Enter a template name.", "Invalid input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TemplateName = NameBox.Text.Trim();
        WidthMm      = w;
        HeightMm     = h;
        DialogResult = true;
    }
}
