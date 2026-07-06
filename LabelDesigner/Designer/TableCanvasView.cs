using LabelDesigner.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace LabelDesigner.Designer;

/// <summary>
/// Canvas visual for a table element. Renders through the SAME <see cref="Helpers.TableLayout"/>
/// the print path uses (render parity), rebuilding whenever the table's structure or styling
/// changes. All subscriptions use named handlers and are detached on unload (no-leak rule);
/// re-attach on load handles items that get removed and re-added to the canvas.
/// </summary>
public class TableCanvasView : ContentControl
{
    private readonly TableElementViewModel _vm;
    private readonly List<INotifyPropertyChanged> _observedItems = new();
    private readonly List<INotifyCollectionChanged> _observedCollections = new();
    private bool _attached;

    // VM properties that change how the table renders.
    private static readonly HashSet<string> RenderProps = new()
    {
        nameof(TableElementViewModel.ShowHeader),
        nameof(TableElementViewModel.HeaderBackground),
        nameof(TableElementViewModel.CellBackground),
        nameof(TableElementViewModel.BorderColor),
        nameof(TableElementViewModel.TableBorderThickness),
        nameof(TableElementViewModel.TableFontFamily),
        nameof(TableElementViewModel.HeaderFontSize),
        nameof(TableElementViewModel.CellFontSize),
    };

    public TableCanvasView(TableElementViewModel vm)
    {
        _vm = vm;
        Focusable = false;
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
        Attach();
        Rebuild();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        Rebuild();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void Attach()
    {
        if (_attached) return;
        _attached = true;

        _vm.PropertyChanged += OnVmPropertyChanged;
        ObserveCollection(_vm.Columns);
        ObserveCollection(_vm.Rows);
        foreach (var col in _vm.Columns) ObserveItem(col);
        foreach (var row in _vm.Rows)
        {
            ObserveCollection(row.Cells);
            foreach (var cell in row.Cells) ObserveItem(cell);
        }
    }

    private void Detach()
    {
        if (!_attached) return;
        _attached = false;

        _vm.PropertyChanged -= OnVmPropertyChanged;
        foreach (var c in _observedCollections) c.CollectionChanged -= OnStructureChanged;
        foreach (var i in _observedItems) i.PropertyChanged -= OnItemPropertyChanged;
        _observedCollections.Clear();
        _observedItems.Clear();
    }

    private void ObserveCollection(INotifyCollectionChanged c)
    {
        c.CollectionChanged += OnStructureChanged;
        _observedCollections.Add(c);
    }

    private void ObserveItem(INotifyPropertyChanged i)
    {
        i.PropertyChanged += OnItemPropertyChanged;
        _observedItems.Add(i);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null && RenderProps.Contains(e.PropertyName)) Rebuild();
    }

    /// <summary>Rows/columns/cells added or removed — re-observe everything, then rebuild.</summary>
    private void OnStructureChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Detach();
        Attach();
        Rebuild();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Content = Helpers.TableLayout.Build(
            _vm.Columns.Select(c => (c.Header, c.Width)).ToList(),
            _vm.Rows.Select(r => (IReadOnlyList<string>)r.Cells.Select(c => c.Value).ToList()).ToList(),
            _vm.ShowHeader, _vm.HeaderBrush, _vm.CellBrush, _vm.BorderBrush,
            _vm.TableBorderThickness, _vm.TableFontFamily, _vm.HeaderFontSize, _vm.CellFontSize);
    }
}
