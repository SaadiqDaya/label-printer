using LabelDesigner.Core.Models;
using LabelDesigner.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LabelDesigner.ViewModels;

public abstract class ElementViewModelBase : ViewModelBase
{
    private double _x, _y, _width = 80, _height = 20;
    private bool _isSelected;
    private int _zIndex;
    private int _effectiveZIndex;
    private string? _printCondition;
    private Guid? _layerId;
    private bool _isLayerVisible = true;
    private bool _isLayerHidden;
    private string _backgroundColor = "Transparent";

    public Guid Id { get; set; } = Guid.NewGuid();
    public abstract ElementType ElementType { get; }

    /// <summary>Field names available for binding — populated by DesignerViewModel from the current template.</summary>
    public ObservableCollection<string> AvailableFields { get; } = new() { "" };

    /// <summary>Layers available for assignment — populated by DesignerViewModel.</summary>
    public ObservableCollection<LayerViewModel> AvailableLayers { get; } = new();

    /// <summary>Short label shown in the Element Explorer tab.</summary>
    public virtual string DisplayName => ElementType.ToString();

    // ─── Print condition ───────────────────────────────────────────────────────

    /// <summary>Optional expression that controls whether this element is printed. Empty = always print.</summary>
    public string? PrintCondition
    {
        get => _printCondition;
        set
        {
            if (Set(ref _printCondition, value))
                SyncConditionList();
        }
    }

    /// <summary>Individual condition clauses parsed from PrintCondition (split on &amp;&amp;).</summary>
    public ObservableCollection<string> ConditionList { get; } = new();

    private void SyncConditionList()
    {
        ConditionList.Clear();
        foreach (var clause in PrintConditionParser.SplitClauses(_printCondition))
            ConditionList.Add(clause);
    }

    public void AddCondition(string clause)
    {
        if (string.IsNullOrWhiteSpace(clause)) return;
        ConditionList.Add(clause.Trim());
        _printCondition = PrintConditionParser.Join(ConditionList);
        OnPropertyChanged(nameof(PrintCondition));
    }

    public void RemoveCondition(string clause)
    {
        ConditionList.Remove(clause);
        _printCondition = PrintConditionParser.Join(ConditionList);
        OnPropertyChanged(nameof(PrintCondition));
    }

    // ─── Layer ────────────────────────────────────────────────────────────────

    /// <summary>Layer this element belongs to. Null = no specific layer.</summary>
    public Guid? LayerId
    {
        get => _layerId;
        set
        {
            if (Set(ref _layerId, value))
                OnPropertyChanged(nameof(SelectedLayer));
        }
    }

    /// <summary>The layer this element belongs to (for binding in the Properties Panel).</summary>
    public LayerViewModel? SelectedLayer
    {
        get => _layerId.HasValue
            ? AvailableLayers.FirstOrDefault(l => l.Id == _layerId)
            : null;
        set
        {
            LayerId = value?.Id;
            OnPropertyChanged();
        }
    }

    /// <summary>Controls designer canvas opacity: 1.0 when the element's layer is visible, 0.25 when hidden.</summary>
    public bool IsLayerVisible
    {
        get => _isLayerVisible;
        set
        {
            if (Set(ref _isLayerVisible, value))
                OnPropertyChanged(nameof(DesignerOpacity));
        }
    }

    /// <summary>Opacity used by the designer canvas to dim elements on hidden layers.</summary>
    public double DesignerOpacity => _isLayerVisible ? 1.0 : 0.25;

    /// <summary>True when the element's layer is completely hidden from the designer view.</summary>
    public bool IsLayerHidden
    {
        get => _isLayerHidden;
        set
        {
            if (Set(ref _isLayerHidden, value))
                OnPropertyChanged(nameof(DesignerVisibility));
        }
    }

    /// <summary>Visibility used by the designer canvas to fully hide elements on hidden layers.</summary>
    public System.Windows.Visibility DesignerVisibility =>
        _isLayerHidden ? System.Windows.Visibility.Hidden : System.Windows.Visibility.Visible;

    // ─── Position / size / Z ─────────────────────────────────────────────────

    /// <summary>Background fill behind the element. "Transparent" by default.</summary>
    public string BackgroundColor
    {
        get => _backgroundColor;
        set { if (Set(ref _backgroundColor, value)) OnPropertyChanged(nameof(BackgroundBrush)); }
    }

    public System.Windows.Media.SolidColorBrush? BackgroundBrush
    {
        get
        {
            try { return (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(_backgroundColor)!; }
            catch { return null; }
        }
    }

    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double Width  { get => _width;  set { if (Set(ref _width,  Math.Max(2, value))) OnDimensionChanged(); } }
    public double Height { get => _height; set { if (Set(ref _height, Math.Max(1, value))) OnDimensionChanged(); } }

    private double _rotation;
    /// <summary>Clockwise rotation in degrees about the element centre. Applied on the canvas and at print.</summary>
    public double Rotation
    {
        get => _rotation;
        set { if (Set(ref _rotation, ((value % 360) + 360) % 360)) OnPropertyChanged(nameof(RotateTransform)); }
    }

    /// <summary>Canvas RenderTransform derived from <see cref="Rotation"/> (use with RenderTransformOrigin 0.5,0.5).</summary>
    public System.Windows.Media.Transform RotateTransform =>
        _rotation == 0 ? System.Windows.Media.Transform.Identity
                       : new System.Windows.Media.RotateTransform(_rotation);

    /// <summary>Called when Width or Height changes. Override to refresh size-dependent computed properties.</summary>
    protected virtual void OnDimensionChanged() { }

    /// <summary>Push live row data into this element so the designer canvas shows substituted values.</summary>
    public virtual void UpdatePreview(Dictionary<string, string>? fields) { }

    public int ZIndex
    {
        get => _zIndex;
        set => Set(ref _zIndex, value);
    }

    /// <summary>
    /// Effective Z-index that accounts for layer position — set by DesignerViewModel.
    /// Top layer in list gets highest base, so it renders above lower layers.
    /// </summary>
    public int EffectiveZIndex
    {
        get => _effectiveZIndex;
        set => Set(ref _effectiveZIndex, value);
    }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Called by DesignerViewModel after AvailableLayers is rebuilt so SelectedLayer re-resolves.</summary>
    public void RefreshLayerBinding() => OnPropertyChanged(nameof(SelectedLayer));

    public abstract LabelElement ToModel();
    public abstract void FromModel(LabelElement element);

    /// <summary>Factory: creates the correct ViewModel subclass from a model element.</summary>
    public static ElementViewModelBase Create(LabelElement element)
    {
        ElementViewModelBase vm = element switch
        {
            TextElement    => new TextElementViewModel(),
            BarcodeElement => new BarcodeElementViewModel(),
            ImageElement   => new ImageElementViewModel(),
            ShapeElement   => new ShapeElementViewModel(),
            TableElement   => new TableElementViewModel(),
            _ => throw new NotSupportedException($"Unknown element type: {element.GetType().Name}")
        };
        vm.FromModel(element);
        return vm;
    }
}
