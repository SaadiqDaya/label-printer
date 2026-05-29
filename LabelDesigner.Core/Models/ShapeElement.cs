namespace LabelDesigner.Core.Models;

public class ShapeElement : LabelElement
{
    public ShapeType ShapeType { get; set; } = ShapeType.Rectangle;
    public string FillColor { get; set; } = "Transparent";
    public string StrokeColor { get; set; } = "#000000";
    public double StrokeThickness { get; set; } = 1;
    public double CornerRadius { get; set; }

    /// <summary>
    /// For Line shapes only. When false the line goes from the element's top-left (0,0) to bottom-right (W,H).
    /// When true it goes top-right (W,0) to bottom-left (0,H), i.e. the "anti-diagonal" direction.
    /// Set during line draw so users can drag in any quadrant and get the line they expected.
    /// </summary>
    public bool LineReverseY { get; set; } = false;

    public override ElementType Type => ShapeType switch
    {
        ShapeType.Ellipse => ElementType.Ellipse,
        ShapeType.Line => ElementType.Line,
        _ => ElementType.Rectangle
    };

    public override LabelElement Clone() => new ShapeElement
    {
        Id = Guid.NewGuid(), X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        PrintCondition = PrintCondition, LayerId = LayerId, BackgroundColor = BackgroundColor, Rotation = Rotation,
        ShapeType = ShapeType, FillColor = FillColor, StrokeColor = StrokeColor,
        StrokeThickness = StrokeThickness, CornerRadius = CornerRadius,
        LineReverseY = LineReverseY
    };
}
