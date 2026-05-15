using LabelDesigner.Services;
using LabelDesigner.ViewModels;
using System.Windows;

namespace LabelDesigner.Views;

public partial class PrintPreviewWindow : Window
{
    public PrintPreviewWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is not PrintPreviewViewModel vm) return;

        // Render at 2× screen DPI so barcodes and text are sharp
        const double previewDpi = 192;
        var preview = PrintService.RenderPreview(vm.Template, vm.Fields, previewDpi);

        // Display at the label's 96-DPI WPF pixel size; Stretch.Fill maps the high-res
        // bitmap into that space without layout inflation
        PreviewImage.Width  = vm.Template.WidthPx;
        PreviewImage.Height = vm.Template.HeightPx;
        PreviewImage.Source = preview;

        vm.CloseRequested += (_, _) => Close();
    }
}
