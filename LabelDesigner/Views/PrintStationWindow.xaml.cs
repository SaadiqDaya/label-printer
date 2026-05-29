using System.Windows;
using LabelDesigner.ViewModels;

namespace LabelDesigner.Views;

public partial class PrintStationWindow : Window
{
    public PrintStationWindow()
    {
        InitializeComponent();
        DataContext = new PrintStationViewModel();
    }
}
