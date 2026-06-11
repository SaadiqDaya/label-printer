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

        // The VM renders the preview bitmap (Source is bound) — it owns which label is shown so the
        // operator can step through the actual job. Display at the label's 96-DPI WPF pixel size;
        // Stretch.Fill maps the high-res bitmap into that space without layout inflation.
        PreviewImage.Width  = _attachedVm.Template.WidthPx;
        PreviewImage.Height = _attachedVm.Template.HeightPx;

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
