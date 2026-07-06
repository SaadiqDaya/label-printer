namespace LabelDesigner.Core.Models;

public class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer 1";
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Hides the layer on the design canvas for THIS SESSION only — deliberately not persisted
    /// (JsonIgnore) and never consulted by the print path, so a designer's temporary hide can't
    /// leak into the saved template or change what prints.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsHidden { get; set; } = false;

    /// <summary>
    /// Optional print condition. When this evaluates to false, all elements on this layer are skipped at print time.
    /// Syntax: {Field} == "value" | {Field} != "value" | {Field} (non-empty) | !{Field} (empty)
    /// </summary>
    public string? PrintCondition { get; set; }
}
