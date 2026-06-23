using LabelDesigner.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace LabelDesigner.Views;

public partial class PrintPreviewWindow : Window
{
    private PrintPreviewViewModel? _attachedVm;
    /// <summary>True while the preview auto-fits the pane — kept in sync on window/preview resize.
    /// Cleared by 1:1 and the ± buttons (an explicit zoom the user set).</summary>
    private bool _fitMode = true;

    public PrintPreviewWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Detach from previous VM (if any) before binding to the new one — avoids handler buildup
        // when this window is reused.
        if (_attachedVm != null)
        {
            _attachedVm.CloseRequested -= OnCloseRequested;
            _attachedVm.PropertyChanged -= OnVmPropertyChanged;
        }
        _attachedVm = DataContext as PrintPreviewViewModel;
        if (_attachedVm == null) return;

        // The VM renders the preview bitmap and owns the display size (label vs composed sheet) —
        // Source/Width/Height are all bound; Stretch.Fill maps the high-res bitmap into that space.
        _attachedVm.CloseRequested += OnCloseRequested;
        _attachedVm.PropertyChanged += OnVmPropertyChanged;

        // Open fitted to the pane so the label/sheet is big and readable (a 50 mm label at 100%
        // is tiny). 1:1 then visibly drops to actual size. Defer until layout gives the viewport.
        _fitMode = true;
        Dispatcher.BeginInvoke(FitToPane, DispatcherPriority.Loaded);
    }

    /// <summary>When the displayed artwork changes size (label ↔ composed sheet), re-fit if fitting.</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_fitMode && e.PropertyName is nameof(PrintPreviewViewModel.PreviewDisplayWidth)
                                       or nameof(PrintPreviewViewModel.PreviewDisplayHeight))
            Dispatcher.BeginInvoke(FitToPane, DispatcherPriority.Background);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitMode) FitToPane();
    }

    /// <summary>Scales the preview (via the VM's PreviewZoom) so the whole label/sheet fills the pane.</summary>
    private void FitToPane()
    {
        if (_attachedVm == null) return;
        double dw = _attachedVm.PreviewDisplayWidth, dh = _attachedVm.PreviewDisplayHeight;
        if (dw <= 0 || dh <= 0) return;

        // Subtract the Image's 12px margin each side plus a little breathing room.
        double availW = PreviewScroller.ViewportWidth - 28;
        double availH = PreviewScroller.ViewportHeight - 28;
        if (availW <= 0 || availH <= 0) return;

        _attachedVm.PreviewZoom = System.Math.Min(availW / dw, availH / dh);   // VM clamps to [0.25, 4.0]
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = true;
        FitToPane();
    }

    private void Actual_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = false;
        if (_attachedVm != null) _attachedVm.PreviewZoom = 1.0;   // actual size
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = false;
        if (_attachedVm != null) _attachedVm.PreviewZoom *= 1.25;
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = false;
        if (_attachedVm != null) _attachedVm.PreviewZoom /= 1.25;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_attachedVm != null)
        {
            _attachedVm.CloseRequested -= OnCloseRequested;
            _attachedVm.PropertyChanged -= OnVmPropertyChanged;
            _attachedVm = null;
        }
        DataContextChanged -= OnDataContextChanged;
        SizeChanged -= OnSizeChanged;
        base.OnClosed(e);
    }
}
