namespace LabelDesigner.Core.Models;

public enum DataSourceType
{
    CurrentDate,   // Today's date, formatted
    CurrentTime,   // Current time, formatted
    RelativeDate,  // Today + RelativeMonths months + RelativeDays days
    Serial,        // User-set counter (shown as SerialStart, user resets manually)
    FixedValue,    // Always outputs the FixedValue string unchanged
    Formula,       // Evaluates FormulaExpression against the other fields/sources
    DatabaseField  // Mirrors a COLUMN of the connected data file (SourceField) under this name
}

/// <summary>How a Serial source behaves across print jobs.</summary>
public enum SerialMode
{
    /// <summary>Counter persists and keeps climbing across jobs/sessions. Needs a stable store;
    /// point DataDir at a shared folder to keep one sequence across multiple stations.</summary>
    Continuous,
    /// <summary>Every print job starts again at SerialStart. No persistence, no shared directory
    /// needed — ideal when each batch is numbered 1..N on its own.</summary>
    ResetPerBatch
}

public class DataSourceDefinition
{
    public Guid   Id            { get; set; } = Guid.NewGuid();
    /// <summary>Name used as the field key, e.g. "PrintDate". Bound to elements just like a regular field.</summary>
    public string Name          { get; set; } = "PrintDate";
    public DataSourceType Type  { get; set; } = DataSourceType.CurrentDate;
    /// <summary>C# format string, e.g. "dd/MM/yyyy", "HH:mm", "D4".</summary>
    public string Format        { get; set; } = "dd/MM/yyyy";
    public int    RelativeMonths { get; set; } = 0;
    public int    RelativeDays   { get; set; } = 0;
    /// <summary>Starting value shown for Serial type.</summary>
    public int    SerialStart   { get; set; } = 1;
    /// <summary>Amount added to the serial for each successive label in a batch (Serial type). Default 1.</summary>
    public int    Increment     { get; set; } = 1;
    /// <summary>Whether the serial persists across jobs (Continuous) or resets to SerialStart each batch.
    /// Default Continuous preserves the behaviour of templates saved before this option existed.</summary>
    public SerialMode SerialMode { get; set; } = SerialMode.Continuous;
    /// <summary>Text prefixed to the serial, e.g. "CTN-".</summary>
    public string SerialPrefix { get; set; } = "";
    /// <summary>Text appended to the serial, e.g. "-A".</summary>
    public string SerialSuffix { get; set; } = "";
    /// <summary>Number base for the counter: 10 = decimal (uses Format), 36 = alphanumeric 0-9A-Z (uses SerialPadWidth).</summary>
    public int    SerialRadix  { get; set; } = 10;
    /// <summary>Zero-pad width for base-36 serials (e.g. 4 → "000A"). Ignored for decimal (Format controls that).</summary>
    public int    SerialPadWidth { get; set; } = 0;
    /// <summary>Static string returned when Type == FixedValue.</summary>
    public string FixedValue    { get; set; } = "";
    /// <summary>Expression evaluated against other fields when Type == Formula, e.g. UPPER({sku}) &amp; "-" &amp; {lot}.</summary>
    public string FormulaExpression { get; set; } = "";

    /// <summary>
    /// Column/field of the connected data file mirrored when Type == DatabaseField. Elements bind to
    /// this source's NAME, so when the data file's column changes you re-point ONE source instead of
    /// editing every element that uses it.
    /// </summary>
    public string SourceField { get; set; } = "";
}
