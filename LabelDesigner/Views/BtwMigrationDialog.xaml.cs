using LabelDesigner.ViewModels;
using System.Windows;

namespace LabelDesigner.Views;

public partial class BtwMigrationDialog : Window
{
    private readonly BtwMigrationViewModel _vm = new();

    /// <summary>Set when the user chose "Open in Designer" — the owner opens it after ShowDialog returns.</summary>
    public string? TemplateToOpen => _vm.TemplateToOpen;

    public BtwMigrationDialog()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.RequestClose += OnRequestClose;
        Closed += OnClosed;
    }

    private void OnRequestClose(object? sender, EventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _vm.RequestClose -= OnRequestClose;
        Closed -= OnClosed;
    }
}
