using System.Text.Json.Serialization;

namespace LabelDesigner.Core.Models;

[JsonDerivedType(typeof(TextElement), "Text")]
[JsonDerivedType(typeof(BarcodeElement), "Barcode")]
[JsonDerivedType(typeof(ImageElement), "Image")]
[JsonDerivedType(typeof(ShapeElement), "Shape")]
public abstract class LabelElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 80;
    public double Height { get; set; } = 20;
    public int ZIndex { get; set; }

    /// <summary>
    /// Optional print condition. Element is skipped when this evaluates to false.
    /// Syntax: {Field} == "value" | {Field} != "value" | {Field} &gt; 0 | {Field} (non-empty) | !{Field} (empty)
    /// </summary>
    public string? PrintCondition { get; set; }

    [JsonIgnore]
    public abstract ElementType Type { get; }

    public abstract LabelElement Clone();
}
