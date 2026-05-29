using System.Windows;
using LabelDesigner.ViewModels;

namespace LabelDesigner.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        _vm = new SettingsViewModel();
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
