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

        // Render preview at screen DPI
        var preview = PrintService.RenderPreview(vm.Template, vm.Fields);
        PreviewImage.Source = preview;

        vm.CloseRequested += (_, _) => Close();
    }
}
