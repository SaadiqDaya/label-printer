namespace LabelDesigner.Core.Models;

public class ShapeElement : LabelElement
{
    public ShapeType ShapeType { get; set; } = ShapeType.Rectangle;
    public string FillColor { get; set; } = "Transparent";
    public string StrokeColor { get; set; } = "#000000";
    public double StrokeThickness { get; set; } = 1;
    public double CornerRadius { get; set; }

    public override ElementType Type => ShapeType switch
    {
        ShapeType.Ellipse => ElementType.Ellipse,
        ShapeType.Line => ElementType.Line,
        _ => ElementType.Rectangle
    };

    public override LabelElement Clone() => new ShapeElement
    {
        Id = Guid.NewGuid(), X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        ShapeType = ShapeType, FillColor = FillColor, StrokeColor = StrokeColor,
        StrokeThickness = StrokeThickness, CornerRadius = CornerRadius
    };
}
