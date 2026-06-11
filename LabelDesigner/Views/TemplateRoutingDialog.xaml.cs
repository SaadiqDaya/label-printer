using System.Windows;
using LabelDesigner.ViewModels;

namespace LabelDesigner.Views;

public partial class TemplateRoutingDialog : Window
{
    private readonly TemplateRoutingViewModel _vm;

    public TemplateRoutingDialog()
    {
        InitializeComponent();
        _vm = new TemplateRoutingViewModel();
        DataContext = _vm;
        _vm.CloseRequested += OnCloseRequested;   // named handler so OnClosed can detach it
    }

    private void OnCloseRequested(object? sender, bool saved)
    {
        DialogResult = saved;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.CloseRequested -= OnCloseRequested;
        base.OnClosed(e);
    }
}
