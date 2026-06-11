using LabelDesigner.ViewModels;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace LabelDesigner.Views;

public partial class PropertiesPanel : UserControl
{
    // ── Native ChooseColor dialog (comdlg32) ──────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct CHOOSECOLOR
    {
        public int    lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public int    rgbResult;
        public IntPtr lpCustColors;
        public int    Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
    }

    [DllImport("comdlg32.dll", SetLastError = true)]
    private static extern bool ChooseColor(ref CHOOSECOLOR cc);

    private const int CC_RGBINIT  = 0x0001;
    private const int CC_FULLOPEN = 0x0002;

    // Instance-scoped so two simultaneous color-picker dialogs (in different panels) can't
    // race on the same buffer. ChooseColor is single-threaded per call site anyway.
    private readonly int[] _customColors = new int[16];

    // ─────────────────────────────────────────────────────────────────────

    public PropertiesPanel() => InitializeComponent();

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ImageElementViewModel vm) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*"
        };
        if (dlg.ShowDialog() == true) vm.ImagePath = dlg.FileName;
    }

    private void InsertCondition_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModelBase vm) return;

        var field = CondFieldPicker.SelectedItem as string ?? "";
        var op    = ((ComboBoxItem?)CondOpPicker.SelectedItem)?.Content?.ToString() ?? "==";
        var value = CondValueBox.Text ?? "";

        var clause = Services.ConditionClauseBuilder.Build(field, op, value, out var error);
        if (error != null) { MessageBox.Show(error, "Print condition"); return; }
        if (!string.IsNullOrEmpty(clause))
            vm.AddCondition(clause);
    }

    private void RemoveCondition_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModelBase vm) return;
        var clause = ((FrameworkElement)sender).Tag?.ToString();
        if (!string.IsNullOrEmpty(clause))
            vm.RemoveCondition(clause);
    }

    private void PickTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TextElementViewModel vm)
        {
            var picked = ShowColorPicker(vm.Color);
            if (picked != null) vm.Color = picked;
        }
    }

    private void PickBackgroundColor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ElementViewModelBase vm)
        {
            var current = vm.BackgroundColor == "Transparent" ? "#FFFFFF" : vm.BackgroundColor;
            var picked = ShowColorPicker(current);
            if (picked != null) vm.BackgroundColor = picked;
        }
    }

    private void PickFillColor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShapeElementViewModel vm)
        {
            var current = vm.FillColor == "Transparent" ? "#FFFFFF" : vm.FillColor;
            var picked = ShowColorPicker(current);
            if (picked != null) vm.FillColor = picked;
        }
    }

    private void PickStrokeColor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShapeElementViewModel vm)
        {
            var picked = ShowColorPicker(vm.StrokeColor);
            if (picked != null) vm.StrokeColor = picked;
        }
    }

    private string? ShowColorPicker(string currentColor)
    {
        int initial = 0;
        try
        {
            // BrushConverter.ConvertFromString can return null or a non-SolidColorBrush — use `as`
            // and fall back to black rather than NRE'ing on malformed input.
            if (new BrushConverter().ConvertFromString(currentColor) is SolidColorBrush brush)
            {
                var c = brush.Color;
                initial = c.R | (c.G << 8) | (c.B << 16);
            }
        }
        catch { }

        var owner = Window.GetWindow(this);
        var hwnd  = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;

        var handle = GCHandle.Alloc(_customColors, GCHandleType.Pinned);
        try
        {
            var cc = new CHOOSECOLOR
            {
                lStructSize  = Marshal.SizeOf<CHOOSECOLOR>(),
                hwndOwner    = hwnd,
                rgbResult    = initial,
                lpCustColors = handle.AddrOfPinnedObject(),
                Flags        = CC_RGBINIT | CC_FULLOPEN
            };

            if (ChooseColor(ref cc))
            {
                int rgb = cc.rgbResult;
                byte r  = (byte)( rgb        & 0xFF);
                byte g  = (byte)((rgb >>  8) & 0xFF);
                byte b  = (byte)((rgb >> 16) & 0xFF);
                return $"#{r:X2}{g:X2}{b:X2}";
            }
        }
        finally
        {
            handle.Free();
        }
        return null;
    }
}
