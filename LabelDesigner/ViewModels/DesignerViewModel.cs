using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Designer;
using LabelDesigner.Services;
using System.Collections.ObjectModel;
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

    public event EventHandler<ElementViewModelBase>? ElementAdded;
    public event EventHandler<ElementViewModelBase>? ElementRemoved;
    public event EventHandler? CanvasCleared;

    public ObservableCollection<ElementViewModelBase> Elements { get; } = new();

    // ─── Undo / Redo ────────────────────────────────────────────────────────
    public UndoRedoManager UndoManager { get; } = new();
    public ICommand UndoCommand  => new RelayCommand(() => UndoManager.Undo(), () => UndoManager.CanUndo);
    public ICommand RedoCommand  => new RelayCommand(() => UndoManager.Redo(), () => UndoManager.CanRedo);

    // ─── Copy / Paste ────────────────────────────────────────────────────────
    public ICommand CopyCommand  => new RelayCommand(CopySelected, () => _selectedElement != null);
    public ICommand PasteCommand => new RelayCommand(PasteClipboard, () => _clipboard != null);

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
        var vm = ElementViewModelBase.Create(model);
        vm.X += 10;
        vm.Y += 10;
        AddElement(vm);
    }

    // ─── Snap to grid ────────────────────────────────────────────────────────
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => Set(ref _snapToGrid, value);
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
    public ICommand ZoomInCommand   => new RelayCommand(() => ViewportScale = Math.Round(ViewportScale + 0.5, 1));
    public ICommand ZoomOutCommand  => new RelayCommand(() => ViewportScale = Math.Round(ViewportScale - 0.5, 1), () => ViewportScale > 0.5);
    public ICommand ZoomResetCommand => new RelayCommand(() => ViewportScale = 4.0);

    public ElementViewModelBase? SelectedElement
    {
        get => _selectedElement;
        set => Set(ref _selectedElement, value);
    }

    // ─── Element commands ────────────────────────────────────────────────────
    public ICommand AddTextCommand      => new RelayCommand(() => AddElement(new TextElementViewModel { X = 10, Y = 10 }));
    public ICommand AddBarcodeCommand   => new RelayCommand(() => AddElement(new BarcodeElementViewModel { X = 10, Y = 30, Width = 120, Height = 40 }));
    public ICommand AddImageCommand     => new RelayCommand(() => AddElement(new ImageElementViewModel { X = 10, Y = 10, Width = 60, Height = 40 }));
    public ICommand AddRectangleCommand => new RelayCommand(() => AddElement(new ShapeElementViewModel { X = 10, Y = 10, Width = 80, Height = 30 }));
    public ICommand DeleteSelectedCommand => new RelayCommand(DeleteSelected, () => _selectedElement != null);

    public void AddElement(ElementViewModelBase vm, bool recordUndo = true)
    {
        SyncElementFields(vm);
        Elements.Add(vm);
        ElementAdded?.Invoke(this, vm);
        SelectedElement = vm;
        if (recordUndo) UndoManager.Push(new AddElementAction(vm, this));
    }

    public void DeleteSelected()
    {
        if (_selectedElement == null) return;
        var vm = _selectedElement;
        Elements.Remove(vm);
        ElementRemoved?.Invoke(this, vm);
        SelectedElement = null;
        UndoManager.Push(new RemoveElementAction(vm, this));
    }

    /// <summary>Remove a specific element without recording undo (used by undo/redo actions).</summary>
    public void RemoveElement(ElementViewModelBase vm)
    {
        Elements.Remove(vm);
        ElementRemoved?.Invoke(this, vm);
        if (SelectedElement == vm) SelectedElement = null;
    }

    public void SelectElement(ElementViewModelBase? vm)
    {
        foreach (var e in Elements) e.IsSelected = false;
        SelectedElement = vm;
        if (vm != null) vm.IsSelected = true;
    }

    // ─── Load / Save ─────────────────────────────────────────────────────────
    public void LoadTemplate(LabelTemplate template, string? filePath = null)
    {
        Elements.Clear();
        CanvasCleared?.Invoke(this, EventArgs.Empty);

        Template = template;
        CurrentFilePath = filePath;

        foreach (var element in template.Elements)
        {
            var vm = ElementViewModelBase.Create(element);
            Elements.Add(vm);
            ElementAdded?.Invoke(this, vm);
        }

        SyncAvailableFields();
        UndoManager.Clear();
    }

    public LabelTemplate ToModel()
    {
        _template.Elements = Elements.Select(vm => vm.ToModel()).ToList();
        return _template;
    }

    public void NewTemplate(double widthMm = 100, double heightMm = 50,
                            string name = "New Template", List<string>? fields = null)
    {
        Elements.Clear();
        CanvasCleared?.Invoke(this, EventArgs.Empty);
        Template = new LabelTemplate
        {
            WidthMm  = widthMm,
            HeightMm = heightMm,
            Name     = name,
            Fields   = fields ?? new List<string>()
        };
        CurrentFilePath = null;
        SelectedElement = null;
        SyncAvailableFields();
        UndoManager.Clear();
    }

    // ─── Field sync ───────────────────────────────────────────────────────────
    /// <summary>
    /// Pushes the current template's field list to every element VM so the
    /// Properties Panel can show a dropdown of available bound fields.
    /// Call after loading a template, changing fields via Manage Fields, or adding elements.
    /// </summary>
    public void SyncAvailableFields()
    {
        foreach (var el in Elements)
            SyncElementFields(el);
    }

    private void SyncElementFields(ElementViewModelBase vm)
    {
        vm.AvailableFields.Clear();
        vm.AvailableFields.Add(""); // empty = not bound
        foreach (var f in _template.Fields)
            vm.AvailableFields.Add(f);
    }

    // ─── Resize ───────────────────────────────────────────────────────────────
    /// <summary>Resize the canvas without creating a new template (preserves elements and fields).</summary>
    public void ResizeCanvas(double widthMm, double heightMm)
    {
        WidthMm  = widthMm;
        HeightMm = heightMm;
    }
}
