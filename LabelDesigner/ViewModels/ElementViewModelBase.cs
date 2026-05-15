using LabelDesigner.Core.Models;
using System.Collections.ObjectModel;

namespace LabelDesigner.ViewModels;

public abstract class ElementViewModelBase : ViewModelBase
{
    private double _x, _y, _width = 80, _height = 20;
    private bool _isSelected;
    private int _zIndex;

    public Guid Id { get; set; } = Guid.NewGuid();
    public abstract ElementType ElementType { get; }

    /// <summary>Field names available for binding — populated by DesignerViewModel from the current template.</summary>
    public ObservableCollection<string> AvailableFields { get; } = new() { "" };

    /// <summary>Short label shown in the Element Explorer tab.</summary>
    public virtual string DisplayName => ElementType.ToString();

    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double Width { get => _width; set => Set(ref _width, Math.Max(10, value)); }
    public double Height { get => _height; set => Set(ref _height, Math.Max(8, value)); }
    public int ZIndex { get => _zIndex; set => Set(ref _zIndex, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public abstract LabelElement ToModel();
    public abstract void FromModel(LabelElement element);

    /// <summary>Factory: creates the correct ViewModel subclass from a model element.</summary>
    public static ElementViewModelBase Create(LabelElement element)
    {
        ElementViewModelBase vm = element switch
        {
            TextElement => new TextElementViewModel(),
            BarcodeElement => new BarcodeElementViewModel(),
            ImageElement => new ImageElementViewModel(),
            ShapeElement => new ShapeElementViewModel(),
            _ => throw new NotSupportedException($"Unknown element type: {element.GetType().Name}")
        };
        vm.FromModel(element);
        return vm;
    }
}
