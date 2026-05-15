using System.Windows;

namespace LabelDesigner.Views;

public partial class ResizeCanvasDlg : Window
{
    public double WidthMm  { get; private set; }
    public double HeightMm { get; private set; }

    public ResizeCanvasDlg(double currentWidthMm, double currentHeightMm)
    {
        InitializeComponent();
        WidthBox.Text  = currentWidthMm.ToString("F1");
        HeightBox.Text = currentHeightMm.ToString("F1");
    }

    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(WidthBox.Text, out var w) || w <= 0)  { MessageBox.Show("Invalid width.");  return; }
        if (!double.TryParse(HeightBox.Text, out var h) || h <= 0) { MessageBox.Show("Invalid height."); return; }
        WidthMm  = w;
        HeightMm = h;
        DialogResult = true;
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((FrameworkElement)sender).Tag.ToString()!.Split(',');
        WidthBox.Text  = parts[0];
        HeightBox.Text = parts[1];
    }
}
