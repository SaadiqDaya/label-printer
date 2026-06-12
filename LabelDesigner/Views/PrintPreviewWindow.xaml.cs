using LabelDesigner.ViewModels;
using System.Windows;

namespace LabelDesigner.Views;

public partial class PrintPreviewWindow : Window
{
    private PrintPreviewViewModel? _attachedVm;

    public PrintPreviewWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Detach from previous VM (if any) before binding to the new one — avoids handler buildup
        // when this window is reused.
        if (_attachedVm != null) _attachedVm.CloseRequested -= OnCloseRequested;
        _attachedVm = DataContext as PrintPreviewViewModel;
        if (_attachedVm == null) return;

        // The VM renders the preview bitmap and owns the display size (label vs composed sheet) —
        // Source/Width/Height are all bound; Stretch.Fill maps the high-res bitmap into that space.
        _attachedVm.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_attachedVm != null)
        {
            _attachedVm.CloseRequested -= OnCloseRequested;
            _attachedVm = null;
        }
        DataContextChanged -= OnDataContextChanged;
        base.OnClosed(e);
    }
}
