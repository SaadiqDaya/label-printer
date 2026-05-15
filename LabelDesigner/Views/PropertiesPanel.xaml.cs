using LabelDesigner.ViewModels;
using Microsoft.Win32;
using System.Windows.Controls;

namespace LabelDesigner.Views;

public partial class PropertiesPanel : UserControl
{
    public PropertiesPanel()
    {
        InitializeComponent();
    }

    private void BrowseImage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not ImageElementViewModel vm) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*"
        };
        if (dlg.ShowDialog() == true) vm.ImagePath = dlg.FileName;
    }
}
