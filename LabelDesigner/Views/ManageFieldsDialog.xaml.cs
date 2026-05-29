using LabelDesigner.ViewModels;
using System.Windows;

namespace LabelDesigner.Views;

public partial class ManageFieldsDialog : Window
{
    public ManageFieldsDialog(ManageFieldsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        // XAML asks for CenterOwner — set the owner so the dialog actually centers on the main window.
        Owner = Application.Current?.MainWindow;
    }

    private void OK_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
