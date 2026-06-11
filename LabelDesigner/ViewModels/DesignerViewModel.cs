using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Designer;
using LabelDesigner.Helpers;
using LabelDesigner.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

public class DesignerViewModel : ViewModelBase
{
    private LabelTemplate _template = new();
    private ElementViewModelBase? _selectedElement;
    private string? _currentFilePath;
    private double _viewportScale = 4.0;
    private bool _snapToGrid = false;
    private double _gridSize = 5.0;
    private ElementViewModelBase? _clipboard;
    private LayerViewModel? _selectedLayer;
    private DataSourceViewModel? _selectedDataSource;
    private bool _isDirty;

    // ─── Live Excel row data ──────────────────────────────────────────────────
    private List<ExcelRow>? _rows;
    private int _currentRowIndex = -1;

    public event EventHandler<ElementViewModelBase>? ElementAdded;
    public event EventHandler<ElementViewModelBase>? ElementRemoved;
    public event EventHandler? CanvasCleared;

    public ObservableCollection<ElementViewModelBase> Elements { get; } = new();
    public ObservableCollection<LayerViewModel> Layers { get; } = new();
    public ObservableCollection<DataSourceViewModel> DataSources { get; } = new();

    /// <summary>
    /// Current multi-selection set. Empty when only a single item is selected
    /// (use <see cref="SelectedElement"/> for the primary). Owned by DesignerCanvas
    /// — the canvas mirrors its internal HashSet into here so commands can iterate.
    /// </summary>
    public ObservableCollection<ElementViewModelBase> SelectedElements { get; } = new();

    /// <summary>True when there are unsaved changes since the last load/save/new.</summary>
    public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }

    // ─── Undo / Redo ────────────────────────────────────────────────────────
    public UndoRedoManager UndoManager { get; } = new();
    public ICommand UndoCommand  => new RelayCommand(() => UndoManager.Undo(), () => UndoManager.CanUndo);
    public ICommand RedoCommand  => new RelayCommand(() => UndoManager.Redo(), () => UndoManager.CanRedo);

    public DesignerViewModel()
    {
        // Any push/undo/redo means there are unsaved changes
        UndoManager.StateChanged += (_, _) =>
        {
            if (UndoManager.CanUndo || UndoManager.CanRedo)
                IsDirty = true;
        };
    }

    // ─── Layer commands ──────────────────────────────────────────────────────
    public LayerViewModel? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (Set(ref _selectedLayer, value))
                CommandManager.InvalidateRequerySuggested(); // refresh DeleteLayer/MoveLayer*
        }
    }

    // ─── Data source commands ─────────────────────────────────────────────────
    public DataSourceViewModel? SelectedDataSource
    {
        get => _selectedDataSource;
        set
        {
            if (Set(ref _selectedDataSource, value))
                CommandManager.InvalidateRequerySuggested(); // refresh RemoveDataSource
        }
    }

    public ICommand AddDataSourceCommand    => new RelayCommand(AddDataSource);
    public ICommand RemoveDataSourceCommand => new RelayCommand(RemoveDataSource, () => _selectedDataSource != null);

    // Named handlers so we can detach later (lambdas can't be removed).
    private void OnDataSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Both: refresh the BoundField dropdowns (Name might have changed) AND re-push the
        // resolved values to every element so the canvas shows the new data-source output.
        SyncAvailableFields();
        RefreshAllPreviews();
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ElementViewModelBase.ZIndex))
            UpdateEffectiveZIndices();
    }

    private void AttachElement(ElementViewModelBase vm)
        => vm.PropertyChanged += OnElementPropertyChanged;

    private void DetachElement(ElementViewModelBase vm)
        => vm.PropertyChanged -= OnElementPropertyChanged;

    private void AttachDataSource(DataSourceViewModel ds)
        => ds.PropertyChanged += OnDataSourcePropertyChanged;

    private void DetachDataSource(DataSourceViewModel ds)
        => ds.PropertyChanged -= OnDataSourcePropertyChanged;

    private void AttachLayer(LayerViewModel l)
        => l.VisibilityChanged += OnLayerVisibilityChanged;

    private void DetachLayer(LayerViewModel l)
        => l.VisibilityChanged -= OnLayerVisibilityChanged;

    private void AddDataSource()
    {
        var ds = new DataSourceViewModel(new Core.Models.DataSourceDefinition { Name = $"Source{DataSources.Count + 1}" });
        AttachDataSource(ds);
        DataSources.Add(ds);
        SelectedDataSource = ds;
        SyncAvailableFields();
        RefreshAllPreviews();
    }

    private void RemoveDataSource()
    {
        if (_selectedDataSource == null) return;
        DetachDataSource(_selectedDataSource);
        DataSources.Remove(_selectedDataSource);
        SelectedDataSource = DataSources.FirstOrDefault();
        SyncAvailableFields();
        RefreshAllPreviews();
    }

    /// <summary>
    /// Re-resolves all data sources and re-pushes preview fields to every element.
    /// Called whenever the live-data state changes (row navigation, DS add/remove/edit).
    /// </summary>
    public void RefreshAllPreviews()
    {
        var fields = GetCurrentPreviewFields();
        foreach (var el in Elements) el.UpdatePreview(fields);
    }

    /// <summary>
    /// Builds the merged field dictionary used for canvas preview: current row (if any)
    /// merged with all resolved data-source values. Returns null when nothing is live.
    /// </summary>
    private Dictionary<string, string>? GetCurrentPreviewFields()
    {
        var dsFields = DataSourceResolver.Resolve(DataSources.Select(ds => ds.ToModel()));

        Dictionary<string, string>? result;
        if (_rows != null && _currentRowIndex >= 0 && _currentRowIndex < _rows.Count)
        {
            var merged = new Dictionary<string, string>(_rows[_currentRowIndex].Fields, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in dsFields) merged.TryAdd(kv.Key, kv.Value);
            result = merged;
        }
        else result = dsFields.Count > 0 ? dsFields : null;

        if (result != null)
            DataSourceResolver.ApplyFormulas(DataSources.Select(ds => ds.ToModel()), result);
        return result;
    }

    public ICommand AddLayerCommand      => new RelayCommand(AddLayer);
    public ICommand DeleteLayerCommand   => new RelayCommand(DeleteLayer,   () => _selectedLayer != null && Layers.Count > 1);
    public ICommand MoveLayerUpCommand   => new RelayCommand(MoveLayerUp,   () => _selectedLayer != null && Layers.IndexOf(_selectedLayer) > 0);
    public ICommand MoveLayerDownCommand => new RelayCommand(MoveLayerDown, () => _selectedLayer != null && Layers.IndexOf(_selectedLayer) < Layers.Count - 1);

    private void AddLayer()
    {
        var lvm = new LayerViewModel(new Layer { Name = $"Layer {Layers.Count + 1}" });
        AttachLayer(lvm);
        Layers.Add(lvm);
        SelectedLayer = lvm;
        SyncLayersToAll();
        SyncLayerFields(lvm);
        UpdateEffectiveZIndices();
    }

    private void DeleteLayer()
    {
        if (_selectedLayer == null || Layers.Count <= 1) return;
        var fallback = Layers.First(l => l != _selectedLayer);
        foreach (var el in Elements.Where(e => e.LayerId == _selectedLayer.Id))
            el.LayerId = fallback.Id;
        DetachLayer(_selectedLayer);
        Layers.Remove(_selectedLayer);
        SelectedLayer = Layers.FirstOrDefault();
        SyncLayersToAll();
        UpdateEffectiveZIndices();
    }

    private void MoveLayerUp()
    {
        if (_selectedLayer == null) return;
        var idx = Layers.IndexOf(_selectedLayer);
        if (idx <= 0) return;
        Layers.Move(idx, idx - 1);
        UpdateEffectiveZIndices();
    }

    private void MoveLayerDown()
    {
        if (_selectedLayer == null) return;
        var idx = Layers.IndexOf(_selectedLayer);
        if (idx < 0 || idx >= Layers.Count - 1) return;
        Layers.Move(idx, idx + 1);
        UpdateEffectiveZIndices();
    }

    private void OnLayerVisibilityChanged(object? sender, EventArgs e)
    {
        if (sender is LayerViewModel lvm)
            ApplyLayerVisibility(lvm);
    }

    private void ApplyLayerVisibility(LayerViewModel lvm)
    {
        foreach (var el in Elements.Where(e => e.LayerId == lvm.Id))
        {
            el.IsLayerVisible = lvm.IsVisible;
            el.IsLayerHidden  = lvm.IsHidden;
        }
    }

    private void SyncLayersToAll()
    {
        foreach (var el in Elements)
            SyncLayersToElement(el);
    }

    private void SyncLayersToElement(ElementViewModelBase vm)
    {
        vm.AvailableLayers.Clear();
        foreach (var l in Layers)
            vm.AvailableLayers.Add(l);
        vm.RefreshLayerBinding();
    }

    // ─── Effective Z-index ───────────────────────────────────────────────────
    /// <summary>
    /// Recomputes EffectiveZIndex for every element.
    /// Layer at index 0 (top of list) = frontmost; gets the highest base value.
    /// </summary>
    public void UpdateEffectiveZIndices()
    {
        var layerCount = Layers.Count;
        for (int i = 0; i < layerCount; i++)
        {
            var layerId   = Layers[i].Id;
            var layerBase = (layerCount - i) * 1000;  // top layer gets highest base
            foreach (var el in Elements.Where(e => e.LayerId == layerId))
                el.EffectiveZIndex = layerBase + el.ZIndex;
        }
        // Elements with no layer assignment use ZIndex directly
        foreach (var el in Elements.Where(e => !e.LayerId.HasValue))
            el.EffectiveZIndex = el.ZIndex;

        ZIndicesUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fired after EffectiveZIndex values are recalculated — the canvas subscribes to force a re-render.</summary>
    public event EventHandler? ZIndicesUpdated;

    // ─── Live row navigation ─────────────────────────────────────────────────
    public bool HasConnectedFile => !string.IsNullOrEmpty(_template?.DefaultExcelPath);
    public bool HasRows          => (_rows?.Count ?? 0) > 0;
    public int  RowCount         => _rows?.Count ?? 0;
    public string RowInfo        => HasRows ? $"Record {_currentRowIndex + 1} of {RowCount}" : "No data loaded";

    public ICommand LoadDataCommand    => new RelayCommand(TryLoadExcelData, () => HasConnectedFile);
    public ICommand PreviousRowCommand => new RelayCommand(GoToPrevRow,      () => HasRows && _currentRowIndex > 0);
    public ICommand NextRowCommand     => new RelayCommand(GoToNextRow,      () => HasRows && _currentRowIndex < RowCount - 1);
    public ICommand ClearDataCommand   => new RelayCommand(ClearLiveData,    () => HasRows);
    public ICommand BrowseRecordsCommand => new RelayCommand(BrowseRecords,  () => HasRows);

    /// <summary>1-based record number for "Go to" input. Setter jumps to the row.</summary>
    public int CurrentRowNumber
    {
        get => _currentRowIndex + 1;
        set => GoToRow(value - 1);
    }

    /// <summary>Navigates to the row at the given 0-based index. No-op when out of range.</summary>
    public void GoToRow(int index)
    {
        if (_rows == null || index < 0 || index >= _rows.Count) return;
        _currentRowIndex = index;
        RaiseRowStateChanged();
        ApplyCurrentRow();
    }

    /// <summary>Raised when the user opens the record-browser popup; MainWindow shows the dialog.</summary>
    public event EventHandler? BrowseRecordsRequested;

    private void BrowseRecords() => BrowseRecordsRequested?.Invoke(this, EventArgs.Empty);

    public void TryLoadExcelData()
    {
        if (string.IsNullOrEmpty(_template.DefaultExcelPath) || !File.Exists(_template.DefaultExcelPath))
        {
            ClearLiveData();
            return;
        }
        try
        {
            _rows = DataImporter.Load(_template.DefaultExcelPath, _template);
            _currentRowIndex = _rows.Count > 0 ? 0 : -1;
        }
        catch
        {
            _rows = null;
            _currentRowIndex = -1;
        }
        RaiseRowStateChanged();
        if (_currentRowIndex >= 0) ApplyCurrentRow();
    }

    public void ClearLiveData()
    {
        _rows = null;
        _currentRowIndex = -1;
        var dsFields = DataSourceResolver.Resolve(DataSources.Select(ds => ds.ToModel()));
        foreach (var el in Elements)
            el.UpdatePreview(dsFields.Count > 0 ? dsFields : null);
        RaiseRowStateChanged();
    }

    private void GoToNextRow()
    {
        if (!HasRows || _currentRowIndex >= RowCount - 1) return;
        _currentRowIndex++;
        RaiseRowStateChanged();
        ApplyCurrentRow();
    }

    private void GoToPrevRow()
    {
        if (!HasRows || _currentRowIndex <= 0) return;
        _currentRowIndex--;
        RaiseRowStateChanged();
        ApplyCurrentRow();
    }

    private void ApplyCurrentRow()
    {
        if (_rows == null || _currentRowIndex < 0 || _currentRowIndex >= _rows.Count) return;
        var fields = new Dictionary<string, string>(_rows[_currentRowIndex].Fields, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in DataSourceResolver.Resolve(DataSources.Select(ds => ds.ToModel())))
            fields.TryAdd(kv.Key, kv.Value);
        DataSourceResolver.ApplyFormulas(DataSources.Select(ds => ds.ToModel()), fields);
        foreach (var el in Elements) el.UpdatePreview(fields);
    }

    private void RaiseRowStateChanged()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(RowInfo));
        OnPropertyChanged(nameof(CurrentRowNumber));
    }

    /// <summary>The live field values for the currently displayed row, or null when no data is loaded.</summary>
    public Dictionary<string, string>? CurrentRowFields =>
        _rows != null && _currentRowIndex >= 0 && _currentRowIndex < _rows.Count
        ? _rows[_currentRowIndex].Fields
        : null;

    /// <summary>The print quantity for the currently displayed row (from ExcelRow.PrintQty), or 1 when no data.</summary>
    public int CurrentRowPrintQty =>
        _rows != null && _currentRowIndex >= 0 && _currentRowIndex < _rows.Count
        ? _rows[_currentRowIndex].PrintQty : 1;

    /// <summary>The full row set (or null if nothing's loaded) for "Print all records" mode.</summary>
    public IReadOnlyList<ExcelRow>? AllRows => _rows;

    // ─── Copy / Paste ────────────────────────────────────────────────────────
    public ICommand CopyCommand  => new RelayCommand(CopySelected,    () => _selectedElement != null);
    public ICommand PasteCommand => new RelayCommand(PasteClipboard,  () => _clipboard != null);

    private void CopySelected()
    {
        if (_selectedElement == null) return;
        var model = _selectedElement.ToModel();
        _clipboard = ElementViewModelBase.Create(model);
    }

    private void PasteClipboard()
    {
        if (_clipboard == null) return;
        var model = _clipboard.ToModel();
        model.Id = Guid.NewGuid();
        model.GroupId = null;   // a pasted copy shouldn't silently join the original's group
        var vm = ElementViewModelBase.Create(model);
        vm.X += 10;
        vm.Y += 10;
        AddElement(vm);
    }

    // ─── Duplicate (Ctrl+D) ──────────────────────────────────────────────────
    public ICommand DuplicateCommand => new RelayCommand(DuplicateSelected, () => _selectedElement != null);

    private void DuplicateSelected()
    {
        if (_selectedElement == null) return;
        var model = _selectedElement.ToModel();
        model.Id = Guid.NewGuid();
        model.GroupId = null;   // a duplicate shouldn't silently join the original's group
        var vm = ElementViewModelBase.Create(model);
        vm.X += 10;
        vm.Y += 10;
        AddElement(vm);   // records its own undo
    }

    // ─── Element Z-order ────────────────────────────────────────────────────
    public ICommand MoveElementForwardCommand  => new RelayCommand(MoveElementForward,  () => _selectedElement != null);
    public ICommand MoveElementBackwardCommand => new RelayCommand(MoveElementBackward, () => _selectedElement != null);
    public ICommand BringToFrontCommand        => new RelayCommand(BringToFront,        () => _selectedElement != null);
    public ICommand SendToBackCommand          => new RelayCommand(SendToBack,          () => _selectedElement != null);

    private void MoveElementForward()
    {
        if (_selectedElement == null) return;
        _selectedElement.ZIndex++;
        UpdateEffectiveZIndices();
    }

    private void MoveElementBackward()
    {
        if (_selectedElement == null) return;
        _selectedElement.ZIndex = Math.Max(0, _selectedElement.ZIndex - 1);
        UpdateEffectiveZIndices();
    }

    // ─── Align & distribute (over the multi-selection) ───────────────────────
    public ICommand AlignLeftCommand     => new RelayCommand(() => Align(e => (Sel().Min(s => s.X), e.Y)), CanArrange);
    public ICommand AlignRightCommand    => new RelayCommand(() => Align(e => (Sel().Max(s => s.X + s.Width) - e.Width, e.Y)), CanArrange);
    public ICommand AlignTopCommand      => new RelayCommand(() => Align(e => (e.X, Sel().Min(s => s.Y))), CanArrange);
    public ICommand AlignBottomCommand   => new RelayCommand(() => Align(e => (e.X, Sel().Max(s => s.Y + s.Height) - e.Height)), CanArrange);
    public ICommand AlignCenterHCommand  => new RelayCommand(() => Align(e => (SelCenterX() - e.Width / 2, e.Y)), CanArrange);
    public ICommand AlignCenterVCommand  => new RelayCommand(() => Align(e => (e.X, SelCenterY() - e.Height / 2)), CanArrange);
    public ICommand DistributeHCommand   => new RelayCommand(() => Distribute(horizontal: true), CanDistribute);
    public ICommand DistributeVCommand   => new RelayCommand(() => Distribute(horizontal: false), CanDistribute);

    private List<ElementViewModelBase> Sel() => SelectedElements.ToList();
    private bool CanArrange() => SelectedElements.Count >= 2;
    private bool CanDistribute() => SelectedElements.Count >= 3;
    private double SelCenterX() { var s = Sel(); return (s.Min(e => e.X) + s.Max(e => e.X + e.Width)) / 2; }
    private double SelCenterY() { var s = Sel(); return (s.Min(e => e.Y) + s.Max(e => e.Y + e.Height)) / 2; }

    private void Align(Func<ElementViewModelBase, (double x, double y)> target)
    {
        var sel = Sel();
        if (sel.Count < 2) return;
        var actions = new List<IUndoAction>();
        foreach (var e in sel)
        {
            double ox = e.X, oy = e.Y;
            var (nx, ny) = target(e);
            if (nx == ox && ny == oy) continue;
            e.X = nx; e.Y = ny;
            actions.Add(new MoveAction(e, ox, oy, nx, ny));
        }
        if (actions.Count > 0) UndoManager.Push(new CompositeAction(actions));
    }

    private void Distribute(bool horizontal)
    {
        var sel = horizontal
            ? Sel().OrderBy(e => e.X + e.Width / 2).ToList()
            : Sel().OrderBy(e => e.Y + e.Height / 2).ToList();
        if (sel.Count < 3) return;

        double firstC = horizontal ? sel[0].X + sel[0].Width / 2 : sel[0].Y + sel[0].Height / 2;
        double lastC  = horizontal ? sel[^1].X + sel[^1].Width / 2 : sel[^1].Y + sel[^1].Height / 2;
        double step = (lastC - firstC) / (sel.Count - 1);

        var actions = new List<IUndoAction>();
        for (int i = 1; i < sel.Count - 1; i++)
        {
            var e = sel[i];
            double ox = e.X, oy = e.Y;
            double center = firstC + i * step;
            if (horizontal) e.X = center - e.Width / 2; else e.Y = center - e.Height / 2;
            if (e.X != ox || e.Y != oy) actions.Add(new MoveAction(e, ox, oy, e.X, e.Y));
        }
        if (actions.Count > 0) UndoManager.Push(new CompositeAction(actions));
    }

    // ─── Persistent groups (Ctrl+G / Ctrl+Shift+G) ───────────────────────────
    public ICommand GroupCommand   => new RelayCommand(GroupSelected,   () => SelectedElements.Count >= 2);
    public ICommand UngroupCommand => new RelayCommand(UngroupSelected, CanUngroup);

    private bool CanUngroup() =>
        SelectedElements.Any(e => e.GroupId != null) || _selectedElement?.GroupId != null;

    /// <summary>Gives every selected element the same GroupId so they select and move as one,
    /// saved with the template. Re-grouping an existing group's members just re-keys them.</summary>
    public void GroupSelected()
    {
        var sel = Sel();
        if (sel.Count < 2) return;
        var old = sel.ToDictionary(e => e, e => e.GroupId);
        var gid = Guid.NewGuid();
        foreach (var e in sel) e.GroupId = gid;
        UndoManager.Push(new GroupAction(old, gid));
        IsDirty = true;
    }

    /// <summary>Dissolves every group that has a member in the current selection.</summary>
    public void UngroupSelected()
    {
        var sel = SelectedElements.Count > 0
            ? Sel()
            : (_selectedElement != null ? new List<ElementViewModelBase> { _selectedElement } : new());
        var gids = sel.Where(e => e.GroupId != null).Select(e => e.GroupId!.Value).ToHashSet();
        if (gids.Count == 0) return;

        var members = Elements.Where(e => e.GroupId.HasValue && gids.Contains(e.GroupId.Value)).ToList();
        var old = members.ToDictionary(e => e, e => e.GroupId);
        foreach (var e in members) e.GroupId = null;
        UndoManager.Push(new GroupAction(old, null));
        IsDirty = true;
    }

    private void BringToFront()
    {
        if (_selectedElement == null) return;
        _selectedElement.ZIndex = (Elements.Count > 0 ? Elements.Max(e => e.ZIndex) : 0) + 1;
        UpdateEffectiveZIndices();
    }

    private void SendToBack()
    {
        if (_selectedElement == null) return;
        _selectedElement.ZIndex = (Elements.Count > 0 ? Elements.Min(e => e.ZIndex) : 0) - 1;
        UpdateEffectiveZIndices();
    }

    // ─── Snap to grid ────────────────────────────────────────────────────────
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => Set(ref _snapToGrid, value);
    }

    private bool _smartGuides = true;
    /// <summary>Smart alignment guides while dragging: snaps to other elements' edges/centres and
    /// the canvas edges/centre, drawing pink guide lines at the match.</summary>
    public bool SmartGuides
    {
        get => _smartGuides;
        set => Set(ref _smartGuides, value);
    }

    public double GridSize
    {
        get => _gridSize;
        set => Set(ref _gridSize, Math.Max(1, value));
    }

    // ─── Template ────────────────────────────────────────────────────────────
    public LabelTemplate Template
    {
        get => _template;
        private set
        {
            Set(ref _template, value);
            OnPropertyChanged(nameof(TemplateName));
            OnPropertyChanged(nameof(CanvasWidthPx));
            OnPropertyChanged(nameof(CanvasHeightPx));
        }
    }

    public string TemplateName
    {
        get => _template.Name;
        set { _template.Name = value; OnPropertyChanged(); }
    }

    public double CanvasWidthPx  => LabelTemplate.MmToPixels(_template.WidthMm);
    public double CanvasHeightPx => LabelTemplate.MmToPixels(_template.HeightMm);

    public double WidthMm
    {
        get => _template.WidthMm;
        set { _template.WidthMm = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanvasWidthPx)); }
    }

    public double HeightMm
    {
        get => _template.HeightMm;
        set { _template.HeightMm = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanvasHeightPx)); }
    }

    public string? CurrentFilePath { get => _currentFilePath; set => Set(ref _currentFilePath, value); }

    public double ViewportScale
    {
        get => _viewportScale;
        set { if (Set(ref _viewportScale, Math.Clamp(value, 0.5, 10.0))) OnPropertyChanged(nameof(ZoomPercent)); }
    }
    public int ZoomPercent => (int)(_viewportScale * 100);
    public ICommand ZoomInCommand    => new RelayCommand(() => ViewportScale = Math.Round(ViewportScale + 0.25, 2));
    public ICommand ZoomOutCommand   => new RelayCommand(() => ViewportScale = Math.Round(ViewportScale - 0.25, 2), () => ViewportScale > 0.5);
    public ICommand ZoomResetCommand => new RelayCommand(() => ViewportScale = 4.0);

    public ElementViewModelBase? SelectedElement
    {
        get => _selectedElement;
        set
        {
            if (Set(ref _selectedElement, value))
                CommandManager.InvalidateRequerySuggested(); // refresh Delete/Copy/MoveElement*
        }
    }

    // ─── Element commands ────────────────────────────────────────────────────
    // ─── Line draw mode ──────────────────────────────────────────────────────
    private bool _isLineDrawMode;
    public bool IsLineDrawMode
    {
        get => _isLineDrawMode;
        set => Set(ref _isLineDrawMode, value);
    }

    public ICommand AddTextCommand      => new RelayCommand(() => AddElement(new TextElementViewModel { X = 10, Y = 10 }));
    public ICommand AddBarcodeCommand   => new RelayCommand(() => AddElement(new BarcodeElementViewModel { X = 10, Y = 30, Width = 120, Height = 40 }));
    public ICommand AddImageCommand     => new RelayCommand(() => AddElement(new ImageElementViewModel { X = 10, Y = 10, Width = 60, Height = 40 }));
    public ICommand AddRectangleCommand => new RelayCommand(() => AddElement(new ShapeElementViewModel { X = 10, Y = 10, Width = 80, Height = 30 }));
    public ICommand AddLineCommand      => new RelayCommand(() => IsLineDrawMode = true);
    public ICommand AddTableCommand     => new RelayCommand(() => AddElement(new TableElementViewModel
        { X = 10, Y = 10, Width = 200, Height = 50 }));
    public ICommand DeleteSelectedCommand =>
        new RelayCommand(DeleteSelected,
            () => _selectedElement != null || SelectedElements.Count > 0);

    public void AddElement(ElementViewModelBase vm, bool recordUndo = true)
    {
        SyncElementFields(vm);
        SyncLayersToElement(vm);

        if (_selectedLayer != null && vm.LayerId == null)
            vm.LayerId = _selectedLayer.Id;

        if (vm.LayerId.HasValue)
        {
            var layer = Layers.FirstOrDefault(l => l.Id == vm.LayerId);
            if (layer != null)
            {
                vm.IsLayerVisible = layer.IsVisible;
                vm.IsLayerHidden  = layer.IsHidden;
            }
        }

        // Seed the new element's preview with the currently-resolved fields
        // (live row + data sources). RefreshAllPreviews will repush after adding anyway.
        vm.UpdatePreview(GetCurrentPreviewFields());

        AttachElement(vm);

        Elements.Add(vm);
        ElementAdded?.Invoke(this, vm);
        SelectedElement = vm;
        if (recordUndo) UndoManager.Push(new AddElementAction(vm, this));
        IsDirty = true;
        UpdateEffectiveZIndices();
    }

    public void DeleteSelected()
    {
        // Prefer the multi-selection set; fall back to the singular selection.
        // Locked elements are never deleted — unlock them first (Properties panel).
        var toDelete = (SelectedElements.Count > 0
            ? SelectedElements.ToList()
            : (_selectedElement != null ? new List<ElementViewModelBase> { _selectedElement } : new List<ElementViewModelBase>()))
            .Where(e => !e.IsLocked).ToList();

        if (toDelete.Count == 0) return;

        foreach (var vm in toDelete)
        {
            DetachElement(vm);
            Elements.Remove(vm);
            ElementRemoved?.Invoke(this, vm);
            UndoManager.Push(new RemoveElementAction(vm, this));
        }
        SelectedElements.Clear();
        SelectedElement = null;
        IsDirty = true;
    }

    public void RemoveElement(ElementViewModelBase vm)
    {
        DetachElement(vm);
        Elements.Remove(vm);
        ElementRemoved?.Invoke(this, vm);
        if (SelectedElement == vm) SelectedElement = null;
        UpdateEffectiveZIndices();
    }

    private bool _selectionChanging;

    public void SelectElement(ElementViewModelBase? vm)
    {
        if (_selectionChanging) return;
        _selectionChanging = true;
        try
        {
            foreach (var e in Elements) e.IsSelected = false;
            SelectedElement = vm;
            if (vm != null) vm.IsSelected = true;
        }
        finally
        {
            _selectionChanging = false;
        }
    }

    // ─── Load / Save ─────────────────────────────────────────────────────────
    public void LoadTemplate(LabelTemplate template, string? filePath = null)
    {
        // Detach every handler we attached so the old VMs can be GC'd.
        foreach (var vm in Elements) DetachElement(vm);
        Elements.Clear();
        CanvasCleared?.Invoke(this, EventArgs.Empty);

        foreach (var lvm in Layers) DetachLayer(lvm);
        Layers.Clear();

        foreach (var ds in DataSources) DetachDataSource(ds);
        DataSources.Clear();

        Template = template;
        CurrentFilePath = filePath;

        if (template.Layers.Count == 0)
            template.Layers.Add(new Layer { Name = "Layer 1" });

        foreach (var layer in template.Layers)
        {
            var lvm = new LayerViewModel(layer);
            AttachLayer(lvm);
            Layers.Add(lvm);
        }
        SelectedLayer = Layers.FirstOrDefault();

        foreach (var dsDef in template.DataSources)
        {
            var dsVm = new DataSourceViewModel(dsDef);
            AttachDataSource(dsVm);
            DataSources.Add(dsVm);
        }
        SelectedDataSource = DataSources.FirstOrDefault();

        foreach (var element in template.Elements)
        {
            var vm = ElementViewModelBase.Create(element);
            SyncLayersToElement(vm);
            if (vm.LayerId.HasValue)
            {
                var layer = Layers.FirstOrDefault(l => l.Id == vm.LayerId);
                if (layer != null)
                {
                    vm.IsLayerVisible = layer.IsVisible;
                    vm.IsLayerHidden  = layer.IsHidden;
                }
            }
            AttachElement(vm);
            Elements.Add(vm);
            ElementAdded?.Invoke(this, vm);
        }

        SyncAvailableFields();
        UpdateEffectiveZIndices();
        UndoManager.Clear();
        IsDirty = false;

        _rows = null;
        _currentRowIndex = -1;
        OnPropertyChanged(nameof(HasConnectedFile));
        RaiseRowStateChanged();
        if (HasConnectedFile) TryLoadExcelData();
        else RefreshAllPreviews(); // push DS values to elements even without Excel data
    }

    public LabelTemplate ToModel()
    {
        _template.Elements    = Elements.Select(vm => vm.ToModel()).ToList();
        _template.Layers      = Layers.Select(l => l.ToModel()).ToList();
        _template.DataSources = DataSources.Select(ds => ds.ToModel()).ToList();
        return _template;
    }

    public void NewTemplate(double widthMm = 100, double heightMm = 50,
                            string name = "New Template", List<string>? fields = null)
    {
        foreach (var vm in Elements) DetachElement(vm);
        Elements.Clear();
        CanvasCleared?.Invoke(this, EventArgs.Empty);

        foreach (var lvm in Layers) DetachLayer(lvm);
        Layers.Clear();

        foreach (var ds in DataSources) DetachDataSource(ds);
        DataSources.Clear();

        Template = new LabelTemplate
        {
            WidthMm  = widthMm,
            HeightMm = heightMm,
            Name     = name,
            Fields   = fields ?? new List<string>()
        };

        var defaultLayer = new LayerViewModel(new Layer { Name = "Layer 1" });
        AttachLayer(defaultLayer);
        Layers.Add(defaultLayer);
        SelectedLayer = defaultLayer;
        SelectedDataSource = null;

        CurrentFilePath = null;
        SelectedElement = null;
        _rows = null;
        _currentRowIndex = -1;
        SyncAvailableFields();
        UndoManager.Clear();
        IsDirty = false;
        OnPropertyChanged(nameof(HasConnectedFile));
        RaiseRowStateChanged();
    }

    // ─── Field sync ───────────────────────────────────────────────────────────
    public void SyncAvailableFields()
    {
        foreach (var el in Elements)
            SyncElementFields(el);
        foreach (var layer in Layers)
            SyncLayerFields(layer);
    }

    private void SyncElementFields(ElementViewModelBase vm)
    {
        vm.AvailableFields.Clear();
        vm.AvailableFields.Add("");
        foreach (var f in _template.Fields)
            vm.AvailableFields.Add(f);
        foreach (var ds in DataSources)
            if (!string.IsNullOrEmpty(ds.Name) && !vm.AvailableFields.Contains(ds.Name))
                vm.AvailableFields.Add(ds.Name);
    }

    private void SyncLayerFields(LayerViewModel layer)
    {
        layer.AvailableFields.Clear();
        foreach (var f in _template.Fields)
            layer.AvailableFields.Add(f);
        foreach (var ds in DataSources)
            if (!string.IsNullOrEmpty(ds.Name) && !layer.AvailableFields.Contains(ds.Name))
                layer.AvailableFields.Add(ds.Name);
    }

    // ─── Resize ───────────────────────────────────────────────────────────────
    public void ResizeCanvas(double widthMm, double heightMm, int dpi)
    {
        WidthMm  = widthMm;
        HeightMm = heightMm;
        _template.Dpi = dpi;
    }
}
