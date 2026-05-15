using LabelDesigner.Core.Models;
using System.Windows.Media;

namespace LabelDesigner.ViewModels;

public class ShapeElementViewModel : ElementViewModelBase
{
    private ShapeType _shapeType = ShapeType.Rectangle;
    private string _fillColor = "Transparent";
    private string _strokeColor = "#000000";
    private double _strokeThickness = 1;
    private double _cornerRadius;

    public override ElementType ElementType => ElementType.Rectangle;
    public override string DisplayName => $"Shape ({ShapeType})";

    public ShapeType ShapeType
    {
        get => _shapeType;
        set => Set(ref _shapeType, value);
    }

    public string FillColor
    {
        get => _fillColor;
        set { if (Set(ref _fillColor, value)) OnPropertyChanged(nameof(FillBrush)); }
    }

    public string StrokeColor
    {
        get => _strokeColor;
        set { if (Set(ref _strokeColor, value)) OnPropertyChanged(nameof(StrokeBrush)); }
    }

    public double StrokeThickness { get => _strokeThickness; set => Set(ref _strokeThickness, value); }
    public double CornerRadius { get => _cornerRadius; set => Set(ref _cornerRadius, value); }

    public Brush FillBrush
    {
        get
        {
            try { return (Brush)new BrushConverter().ConvertFromString(FillColor)!; }
            catch { return Brushes.Transparent; }
        }
    }

    public Brush StrokeBrush
    {
        get
        {
            try { return (Brush)new BrushConverter().ConvertFromString(StrokeColor)!; }
            catch { return Brushes.Black; }
        }
    }

    public override LabelElement ToModel() => new ShapeElement
    {
        Id = Id, X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        ShapeType = ShapeType, FillColor = FillColor, StrokeColor = StrokeColor,
        StrokeThickness = StrokeThickness, CornerRadius = CornerRadius
    };

    public override void FromModel(LabelElement element)
    {
        var m = (ShapeElement)element;
        Id = m.Id; X = m.X; Y = m.Y; Width = m.Width; Height = m.Height; ZIndex = m.ZIndex;
        ShapeType = m.ShapeType; FillColor = m.FillColor; StrokeColor = m.StrokeColor;
        StrokeThickness = m.StrokeThickness; CornerRadius = m.CornerRadius;
    }
}
