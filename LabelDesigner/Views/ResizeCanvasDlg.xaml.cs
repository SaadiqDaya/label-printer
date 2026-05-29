using System.Windows;
using LabelDesigner.Core.Models;

namespace LabelDesigner.Views;

public partial class ResizeCanvasDlg : Window
{
    public double WidthMm  { get; private set; }
    public double HeightMm { get; private set; }
    public int    Dpi      { get; private set; }

    // Printer profile edits
    public bool    OutputZpl { get; private set; }
    public int?    Darkness  { get; private set; }
    public double? SpeedIps  { get; private set; }
    public string? ZplHost   { get; private set; }

    public ResizeCanvasDlg(double currentWidthMm, double currentHeightMm, int currentDpi, PrinterProfile profile)
    {
        InitializeComponent();
        WidthBox.Text  = currentWidthMm.ToString("F1");
        HeightBox.Text = currentHeightMm.ToString("F1");
        DpiBox.Text    = currentDpi.ToString();
        Dpi            = currentDpi;

        OutputBox.SelectedIndex = profile.OutputMode == PrintBackend.Zpl ? 1 : 0;
        DarknessBox.Text = profile.Darkness?.ToString() ?? "";
        SpeedBox.Text    = profile.SpeedIps?.ToString() ?? "";
        ZplHostBox.Text  = profile.NetworkHost ?? "";
    }

    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(WidthBox.Text, out var w) || w <= 0)  { MessageBox.Show("Invalid width.");  return; }
        if (!double.TryParse(HeightBox.Text, out var h) || h <= 0) { MessageBox.Show("Invalid height."); return; }

        var dpiText = (DpiBox.Text ?? "").Trim();
        if (int.TryParse(dpiText, out var d) && d >= 96) Dpi = d;
        else { MessageBox.Show("Invalid DPI — use 203, 300, or 600."); return; }

        WidthMm  = w;
        HeightMm = h;

        OutputZpl = OutputBox.SelectedIndex == 1;
        Darkness  = int.TryParse(DarknessBox.Text, out var dk) ? Math.Clamp(dk, 0, 30) : (int?)null;
        SpeedIps  = double.TryParse(SpeedBox.Text, out var sp) ? sp : (double?)null;
        ZplHost   = string.IsNullOrWhiteSpace(ZplHostBox.Text) ? null : ZplHostBox.Text.Trim();

        DialogResult = true;
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((FrameworkElement)sender).Tag.ToString()!.Split(',');
        WidthBox.Text  = parts[0];
        HeightBox.Text = parts[1];
    }
}
