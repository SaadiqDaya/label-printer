using LabelDesigner.ViewModels;
using System.Windows;

namespace LabelDesigner.Views;

public partial class ManageFieldsDialog : Window
{
    public ManageFieldsDialog(ManageFieldsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OK_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
