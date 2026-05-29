namespace LabelDesigner.Core.Models;

public enum FieldDataType { Text, Number, Date, Barcode }

/// <summary>
/// Metadata about a template field, used to validate incoming data (IPC jobs, operator prompts)
/// and to drive the Print Station input form. Backward compatible: templates created before this
/// existed carry only <see cref="LabelTemplate.Fields"/> (names) and get default definitions
/// backfilled on load, so a missing definition never blocks an old template.
/// </summary>
public class FieldDefinition
{
    public string Name { get; set; } = "";

    /// <summary>When true, a print job that omits/blanks this field is rejected (or filled with DefaultValue).</summary>
    public bool Required { get; set; } = false;

    /// <summary>Value substituted when the field is absent. Null = leave empty.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Optional .NET format string applied when rendering (e.g. "dd/MM/yyyy", "F2").</summary>
    public string? Format { get; set; }

    public FieldDataType DataType { get; set; } = FieldDataType.Text;

    /// <summary>Operator-facing label shown in the Print Station input form. Falls back to Name when empty.</summary>
    public string? Prompt { get; set; }

    // ── Validation rules (all optional; enforced before print via FieldValidator) ──
    /// <summary>Regex the value must fully match. Null/empty = no pattern check.</summary>
    public string? Pattern { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    /// <summary>Numeric range when DataType = Number. Null = unbounded.</summary>
    public double? Min { get; set; }
    public double? Max { get; set; }
    /// <summary>When non-empty, the value must be one of these (also drives a dropdown in Print Station).</summary>
    public List<string> AllowedValues { get; set; } = new();
}
