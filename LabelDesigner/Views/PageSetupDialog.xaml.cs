using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using System.Globalization;
using System.Windows;

namespace LabelDesigner.Views;

/// <summary>
/// Edits a template's optional <see cref="PageLayout"/> — sheet-mode printing (Avery sheets,
/// cards, menus). <see cref="Result"/> is the new layout after OK (null = sheet mode off).
/// </summary>
public partial class PageSetupDialog : Window
{
    private readonly double _labelWmm, _labelHmm;

    /// <summary>The edited layout (null = sheet mode off). Valid only when DialogResult == true.</summary>
    public PageLayout? Result { get; private set; }

    public PageSetupDialog(PageLayout? current, double labelWidthMm, double labelHeightMm)
    {
        InitializeComponent();
        _labelWmm = labelWidthMm;
        _labelHmm = labelHeightMm;

        // Back-template choices: every template in the library.
        try
        {
            var svc = new TemplateService(AppConfig.TemplatesDir);
            foreach (var t in svc.LoadAll()) BackBox.Items.Add(t.Name);
        }
        catch (Exception ex) { LogService.Warn($"Could not list templates for Page Setup: {ex.Message}"); }

        var p = current ?? new PageLayout();
        EnabledBox.IsChecked = current != null;
        PageWBox.Text   = Num(p.PageWidthMm);
        PageHBox.Text   = Num(p.PageHeightMm);
        ColsBox.Text    = p.Columns.ToString();
        RowsBox.Text    = p.Rows.ToString();
        MarginLBox.Text = Num(p.MarginLeftMm);
        MarginTBox.Text = Num(p.MarginTopMm);
        GutterXBox.Text = Num(p.GutterXMm);
        GutterYBox.Text = Num(p.GutterYMm);
        FillBox.SelectedIndex = p.FillAcrossFirst ? 0 : 1;
        BackBox.Text    = p.BackTemplateName;

        UpdateEnabled();
        UpdateFitInfo();
    }

    private static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryNum(string? s, out double v) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v) ||
        double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out v);

    private void EnabledBox_Changed(object sender, RoutedEventArgs e) => UpdateEnabled();

    private void UpdateEnabled() => LayoutPanel.IsEnabled = EnabledBox.IsChecked == true;

    private void PagePreset_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((FrameworkElement)sender).Tag.ToString()!.Split(',');
        PageWBox.Text = parts[0];
        PageHBox.Text = parts[1];
        UpdateFitInfo();
    }

    private void Avery_Click(object sender, RoutedEventArgs e)
    {
        var a = PageLayout.Avery5160();
        PageWBox.Text = Num(a.PageWidthMm);  PageHBox.Text = Num(a.PageHeightMm);
        ColsBox.Text = a.Columns.ToString(); RowsBox.Text = a.Rows.ToString();
        MarginLBox.Text = Num(a.MarginLeftMm); MarginTBox.Text = Num(a.MarginTopMm);
        GutterXBox.Text = Num(a.GutterXMm);  GutterYBox.Text = Num(a.GutterYMm);
        FillBox.SelectedIndex = 0;
        if (Math.Abs(_labelWmm - 66.7) > 0.5 || Math.Abs(_labelHmm - 25.4) > 0.5)
            MessageBox.Show(this,
                $"Avery 5160 labels are 66.7 × 25.4 mm, but this template is {_labelWmm:0.#} × {_labelHmm:0.#} mm.\n\n" +
                "Resize the label in Template ▸ Resize Canvas so the cells line up.",
                "Label size mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
        UpdateFitInfo();
    }

    private void Center_Click(object sender, RoutedEventArgs e)
    {
        if (!TryNum(PageWBox.Text, out var pw) || !TryNum(PageHBox.Text, out var ph)) return;
        ColsBox.Text = "1"; RowsBox.Text = "1";
        MarginLBox.Text = Num(Math.Max(0, (pw - _labelWmm) / 2));
        MarginTBox.Text = Num(Math.Max(0, (ph - _labelHmm) / 2));
        GutterXBox.Text = "0"; GutterYBox.Text = "0";
        UpdateFitInfo();
    }

    private PageLayout? ReadLayout(out string? error)
    {
        error = null;
        if (!TryNum(PageWBox.Text, out var pw) || pw <= 0 ||
            !TryNum(PageHBox.Text, out var ph) || ph <= 0) { error = "Invalid page size."; return null; }
        if (!int.TryParse(ColsBox.Text, out var cols) || cols < 1 ||
            !int.TryParse(RowsBox.Text, out var rows) || rows < 1) { error = "Rows and columns must be at least 1."; return null; }
        if (!TryNum(MarginLBox.Text, out var ml) || ml < 0 ||
            !TryNum(MarginTBox.Text, out var mt) || mt < 0) { error = "Invalid margins."; return null; }
        if (!TryNum(GutterXBox.Text, out var gx) || gx < 0 ||
            !TryNum(GutterYBox.Text, out var gy) || gy < 0) { error = "Invalid gutters."; return null; }

        return new PageLayout
        {
            PageWidthMm = pw, PageHeightMm = ph,
            Columns = cols, Rows = rows,
            MarginLeftMm = ml, MarginTopMm = mt,
            GutterXMm = gx, GutterYMm = gy,
            FillAcrossFirst = FillBox.SelectedIndex == 0,
            BackTemplateName = (BackBox.Text ?? "").Trim()
        };
    }

    private void UpdateFitInfo()
    {
        var p = ReadLayout(out _);
        if (p == null) { FitInfo.Text = ""; return; }
        double usedW = p.MarginLeftMm + p.Columns * _labelWmm + (p.Columns - 1) * p.GutterXMm;
        double usedH = p.MarginTopMm + p.Rows * _labelHmm + (p.Rows - 1) * p.GutterYMm;
        FitInfo.Text = $"{p.Columns} × {p.Rows} = {p.CellsPerPage} labels per page. " +
                       $"Grid uses {usedW:0.#} × {usedH:0.#} mm of {p.PageWidthMm:0.#} × {p.PageHeightMm:0.#} mm" +
                       (usedW > p.PageWidthMm || usedH > p.PageHeightMm ? "  ⚠ DOESN'T FIT" : "");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (EnabledBox.IsChecked != true)
        {
            Result = null;
            DialogResult = true;
            return;
        }

        var layout = ReadLayout(out var error);
        if (layout == null) { MessageBox.Show(this, error, "Page Setup"); return; }

        double usedW = layout.MarginLeftMm + layout.Columns * _labelWmm + (layout.Columns - 1) * layout.GutterXMm;
        double usedH = layout.MarginTopMm + layout.Rows * _labelHmm + (layout.Rows - 1) * layout.GutterYMm;
        if (usedW > layout.PageWidthMm + 0.01 || usedH > layout.PageHeightMm + 0.01)
        {
            MessageBox.Show(this,
                $"The grid needs {usedW:0.#} × {usedH:0.#} mm but the page is only " +
                $"{layout.PageWidthMm:0.#} × {layout.PageHeightMm:0.#} mm.\n\nReduce rows/columns/margins or enlarge the page.",
                "Grid doesn't fit the page", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = layout;
        DialogResult = true;
    }
}
