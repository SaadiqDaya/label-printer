using LabelDesigner.Services;
using LabelDesigner.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LabelDesigner.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly IpcServer _ipc = new();
    private bool _listSelectionChanging;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // All cross-component event wires use named methods so OnClosed can detach them.
        DesignerView.Canvas.SelectionChanged += OnCanvasSelectionChanged;

        _vm.Designer.ElementAdded    += OnElementAdded;
        _vm.Designer.ElementRemoved  += OnElementRemoved;
        _vm.Designer.CanvasCleared   += OnCanvasCleared;
        _vm.Designer.ZIndicesUpdated += OnZIndicesUpdated;
        _vm.Designer.PropertyChanged += OnDesignerPropertyChanged;
        _vm.Designer.BrowseRecordsRequested += OnBrowseRecordsRequested;

        // Warn the user when the requested printer is missing so we don't silently
        // print to "Microsoft Print to PDF" or the wrong default. PrintService raises
        // this from inside GetPrintQueue, before the fallback queue is returned.
        PrintService.PrinterNotFound += OnPrinterNotFound;

        // Start IPC for JaneERP integration. JobHandler returns a structured outcome that the
        // server logs (by JobId) and acks back to duplex callers.
        _ipc.JobHandler = _vm.HandlePrintJob;
        _ipc.Start();
        _vm.IpcStatus = $"IPC: Active  |  pipe: {IpcServer.PipeName}";

        // ── Keyboard shortcuts ───────────────────────────────────────────
        InputBindings.Add(new KeyBinding(_vm.SaveCommand,                    Key.S,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.OpenCommand,                    Key.O,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.NewCommand,                     Key.N,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.ManageFieldsCommand,            Key.M,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.PrintCommand,                   Key.P,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.DeleteSelectedCommand,          Key.Delete, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(_vm.Designer.UndoCommand,           Key.Z,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.Designer.RedoCommand,           Key.Y,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.Designer.CopyCommand,           Key.C,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.Designer.PasteCommand,          Key.V,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.Designer.DuplicateCommand,       Key.D,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.Designer.GroupCommand,           Key.G,      ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.Designer.UngroupCommand,         Key.G,      ModifierKeys.Control | ModifierKeys.Shift));
    }

    // ─── Named handlers (so OnClosed can -= them) ────────────────────────────
    private void OnCanvasSelectionChanged(object? sender, ElementViewModelBase? vm)
        => _vm.Designer.SelectElement(vm);

    private void OnElementAdded(object? sender, ElementViewModelBase vm)
        => DesignerView.Canvas.AddElement(vm);

    private void OnElementRemoved(object? sender, ElementViewModelBase vm)
        => DesignerView.Canvas.RemoveByViewModel(vm);

    private void OnCanvasCleared(object? sender, EventArgs e)
        => DesignerView.Canvas.ClearAll();

    private void OnZIndicesUpdated(object? sender, EventArgs e)
        => DesignerView.Canvas.ApplyZIndices();

    private void OnDesignerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DesignerViewModel.IsLineDrawMode)) return;
        if (_vm.Designer.IsLineDrawMode)
        {
            DesignerView.Canvas.CurrentTool = Designer.DesignerTool.DrawLine;
            DesignerView.Canvas.Cursor = Cursors.Cross;
            DesignerView.Canvas.Focus();
        }
        else
        {
            DesignerView.Canvas.CurrentTool = Designer.DesignerTool.Select;
            DesignerView.Canvas.Cursor = null;
        }
    }

    private void OnBrowseRecordsRequested(object? sender, EventArgs e)
    {
        var rows = _vm.Designer.AllRows;
        if (rows == null || rows.Count == 0) return;

        var dlg = new RecordBrowserDialog(rows, initialIndex: _vm.Designer.CurrentRowNumber - 1)
        {
            Owner = this
        };
        if (dlg.ShowDialog() == true && dlg.SelectedIndex >= 0)
            _vm.Designer.GoToRow(dlg.SelectedIndex);
    }

    private void OnPrinterNotFound(string requestedPrinter)
    {
        // PrinterNotFound can fire from any thread the print is invoked on
        // (IPC path runs on the dispatcher via HandlePrintJob, but be defensive).
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnPrinterNotFound(requestedPrinter)));
            return;
        }

        _vm.StatusMessage = $"Printer '{requestedPrinter}' not found — used default.";
        MessageBox.Show(
            this,
            $"The requested printer was not found:\n\n    {requestedPrinter}\n\n" +
            "The job will print to the system default printer instead.\n\n" +
            "Check that the printer is connected and the name matches exactly.",
            "Printer not found",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ─── Save-on-close ────────────────────────────────────────────────────────
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.Designer.IsDirty)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Save before closing?",
                "Label Designer",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (result == MessageBoxResult.Yes)
                _vm.SaveTemplate();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Detach in reverse order of attach so the VM/canvas/ipc can be GC'd.
        DesignerView.Canvas.SelectionChanged -= OnCanvasSelectionChanged;
        _vm.Designer.ElementAdded    -= OnElementAdded;
        _vm.Designer.ElementRemoved  -= OnElementRemoved;
        _vm.Designer.CanvasCleared   -= OnCanvasCleared;
        _vm.Designer.ZIndicesUpdated -= OnZIndicesUpdated;
        _vm.Designer.PropertyChanged -= OnDesignerPropertyChanged;
        _vm.Designer.BrowseRecordsRequested -= OnBrowseRecordsRequested;
        _ipc.JobHandler = null;
        PrintService.PrinterNotFound -= OnPrinterNotFound;
        _ipc.Stop();
        base.OnClosed(e);
    }

    // ─── Element list ─────────────────────────────────────────────────────────
    private void ElementList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listSelectionChanging) return;
        _listSelectionChanging = true;
        try
        {
            if (((ListBox)sender).SelectedItem is ElementViewModelBase vm)
                DesignerView.Canvas.SelectByViewModel(vm);
        }
        finally
        {
            _listSelectionChanging = false;
        }
    }

    private void ElementList_RightClick(object sender, MouseButtonEventArgs e)
    {
        var lb = (ListBox)sender;
        var hit = lb.InputHitTest(e.GetPosition(lb)) as DependencyObject;
        while (hit != null && hit is not ListBoxItem)
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        if (hit is ListBoxItem lbi && lbi.Content is ElementViewModelBase vm)
        {
            lb.SelectedItem = vm;
            DesignerView.Canvas.SelectByViewModel(vm);
        }
    }

    // ─── Layer condition builder ──────────────────────────────────────────────
    private void LayerInsertCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Designer.SelectedLayer is not LayerViewModel lvm) return;

        var field = LayerCondFieldPicker.SelectedItem as string ?? "";
        var op    = ((ComboBoxItem?)LayerCondOpPicker.SelectedItem)?.Content?.ToString() ?? "==";
        var value = LayerCondValueBox.Text ?? "";

        var clause = Services.ConditionClauseBuilder.Build(field, op, value, out var error);
        if (error != null) { MessageBox.Show(error, "Print condition"); return; }
        if (!string.IsNullOrEmpty(clause))
            lvm.AddCondition(clause);
    }

    private void LayerRemoveCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Designer.SelectedLayer is not LayerViewModel lvm) return;
        var clause = ((FrameworkElement)sender).Tag?.ToString();
        if (!string.IsNullOrEmpty(clause))
            lvm.RemoveCondition(clause);
    }
}
