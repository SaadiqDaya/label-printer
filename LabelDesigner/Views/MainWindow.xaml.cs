using LabelDesigner.Services;
using LabelDesigner.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LabelDesigner.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly IpcServer _ipc = new();

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Wire canvas selection back to ViewModel
        DesignerView.Canvas.SelectionChanged += (_, vm) => _vm.Designer.SelectElement(vm);

        // Wire ViewModel element events to canvas
        _vm.Designer.ElementAdded   += (_, vm) => DesignerView.Canvas.AddElement(vm);
        _vm.Designer.ElementRemoved += (_, vm) => DesignerView.Canvas.RemoveByViewModel(vm);
        _vm.Designer.CanvasCleared  += (_, _)  => DesignerView.Canvas.ClearAll();

        // Start IPC for JaneERP integration
        _ipc.JobReceived += (_, job) => _vm.HandlePrintJob(job);
        _ipc.Start();
        _vm.IpcStatus = $"IPC: Active  |  pipe: {IpcServer.PipeName}";

        // ── Keyboard shortcuts ───────────────────────────────────────────
        InputBindings.Add(new KeyBinding(_vm.SaveCommand,                    Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.OpenCommand,                    Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.NewCommand,                     Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.ManageFieldsCommand,            Key.M, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.PrintCommand,                   Key.P, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.DeleteSelectedCommand,          Key.Delete, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(_vm.Designer.UndoCommand,           Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.Designer.RedoCommand,           Key.Y, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.Designer.CopyCommand,           Key.C, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.Designer.PasteCommand,          Key.V, ModifierKeys.Control));
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void TemplateList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((ListBox)sender).SelectedItem is TemplateListItem item)
        {
            var service = new LabelDesigner.Core.Services.TemplateService(
                System.IO.Path.GetDirectoryName(item.FilePath)!);
            var template = service.Load(item.FilePath);
            if (template != null) _vm.Designer.LoadTemplate(template, item.FilePath);
        }
    }

    /// <summary>
    /// Ensure the right-clicked item is selected before the context menu opens,
    /// so the context menu commands operate on the correct template.
    /// </summary>
    private void TemplateList_RightClick(object sender, MouseButtonEventArgs e)
    {
        var lb = (ListBox)sender;
        var hit = lb.InputHitTest(e.GetPosition(lb)) as DependencyObject;
        while (hit != null && hit is not ListBoxItem)
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        if (hit is ListBoxItem lbi)
            lb.SelectedItem = lbi.Content;
    }

    protected override void OnClosed(EventArgs e)
    {
        _ipc.Stop();
        base.OnClosed(e);
    }
}
