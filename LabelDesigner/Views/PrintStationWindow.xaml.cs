using System.Windows;
using LabelDesigner.ViewModels;

namespace LabelDesigner.Views;

public partial class PrintStationWindow : Window
{
    public PrintStationWindow()
    {
        InitializeComponent();
        DataContext = new PrintStationViewModel();
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        (DataContext as PrintStationViewModel)?.Dispose();   // stops the watch-folder service
    }
}
