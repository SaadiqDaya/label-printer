namespace LabelDesigner.Core.Models;

public class TextElement : LabelElement
{
    public string Text { get; set; } = "Text";
    /// <summary>When set, this field name is substituted at print time (e.g. "flavor").</summary>
    public string? BoundField { get; set; }
    public string FontFamily { get; set; } = "Arial";
    public double FontSize { get; set; } = 12;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string Color { get; set; } = "#000000";
    public TextAlignmentOption Alignment { get; set; } = TextAlignmentOption.Left;
    public bool FitToBox { get; set; } = false;
    /// <summary>When true, long text wraps onto multiple lines. When false, the element renders as a single line (default).</summary>
    public bool MultiLine { get; set; } = false;

    public override ElementType Type => ElementType.Text;

    public override LabelElement Clone() => new TextElement
    {
        Id = Guid.NewGuid(), X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        PrintCondition = PrintCondition, LayerId = LayerId, BackgroundColor = BackgroundColor, Rotation = Rotation,
        Name = Name, IsLocked = IsLocked,   // GroupId intentionally NOT copied: a clone shouldn't silently join the group
        Text = Text, BoundField = BoundField, FontFamily = FontFamily,
        FontSize = FontSize, Bold = Bold, Italic = Italic, Underline = Underline,
        Color = Color, Alignment = Alignment, FitToBox = FitToBox, MultiLine = MultiLine
    };
}
