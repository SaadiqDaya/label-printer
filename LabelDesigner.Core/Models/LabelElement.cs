using System.Text.Json.Serialization;

namespace LabelDesigner.Core.Models;

[JsonDerivedType(typeof(TextElement), "Text")]
[JsonDerivedType(typeof(BarcodeElement), "Barcode")]
[JsonDerivedType(typeof(ImageElement), "Image")]
[JsonDerivedType(typeof(ShapeElement), "Shape")]
[JsonDerivedType(typeof(TableElement), "Table")]
public abstract class LabelElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }

    private double _width = 80;
    /// <summary>Element width in design pixels. Clamped to a positive value.</summary>
    public double Width
    {
        get => _width;
        set => _width = value > 0 ? value : 1;
    }

    private double _height = 20;
    /// <summary>Element height in design pixels. Clamped to a positive value.</summary>
    public double Height
    {
        get => _height;
        set => _height = value > 0 ? value : 1;
    }

    public int ZIndex { get; set; }

    /// <summary>
    /// Optional print condition. Element is skipped when this evaluates to false.
    /// Syntax: {Field} == "value" | {Field} != "value" | {Field} &gt; 0 | {Field} (non-empty) | !{Field} (empty)
    /// </summary>
    public string? PrintCondition { get; set; }

    /// <summary>Layer this element belongs to. Null = default layer.</summary>
    public Guid? LayerId { get; set; }

    /// <summary>Background fill behind the element. "Transparent" means no fill.</summary>
    public string BackgroundColor { get; set; } = "Transparent";

    /// <summary>
    /// Clockwise rotation in degrees about the element's centre. 0 = upright.
    /// Applied identically on the design canvas and at print time.
    /// </summary>
    public double Rotation { get; set; } = 0;

    /// <summary>Optional user-given name shown in the Element Explorer (e.g. "Lot barcode"). Blank = type-derived label.</summary>
    public string Name { get; set; } = "";

    /// <summary>Locked elements can't be moved, resized or deleted on the design canvas. They still print.</summary>
    public bool IsLocked { get; set; }

    /// <summary>Persistent group membership: elements sharing a GroupId select and move as one. Null = ungrouped.</summary>
    public Guid? GroupId { get; set; }

    [JsonIgnore]
    public abstract ElementType Type { get; }

    public abstract LabelElement Clone();
}
