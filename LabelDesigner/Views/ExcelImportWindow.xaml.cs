using LabelDesigner.Core.Models;
using LabelDesigner.Services;
using LabelDesigner.ViewModels;
using System.Windows;

namespace LabelDesigner.Views;

public partial class ExcelImportWindow : Window
{
    private readonly Action<List<LabelJob>> _printHandler;

    public ExcelImportWindow(ExcelImportViewModel vm, Action<List<LabelJob>> printHandler)
    {
        InitializeComponent();
        _printHandler = printHandler;
        DataContext = vm;
        vm.PrintJobsReady += (_, jobs) =>
        {
            _printHandler(jobs);
            Close();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
