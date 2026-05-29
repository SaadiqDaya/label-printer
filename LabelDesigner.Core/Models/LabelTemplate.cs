using System.Text.Json.Serialization;

namespace LabelDesigner.Core.Models;

public class LabelTemplate : IJsonOnDeserialized
{
    /// <summary>Template schema version. Bumped when persisted shape changes incompatibly. v2 added Dpi/PrinterProfile/FieldDefinitions/Rotation.</summary>
    public int Version { get; set; } = 2;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Template";

    private double _widthMm = 100;
    /// <summary>Label width in millimetres. Clamped to a positive value.</summary>
    public double WidthMm
    {
        get => _widthMm;
        set => _widthMm = value > 0 ? value : 1;
    }

    private double _heightMm = 50;
    /// <summary>Label height in millimetres. Clamped to a positive value.</summary>
    public double HeightMm
    {
        get => _heightMm;
        set => _heightMm = value > 0 ? value : 1;
    }

    public string BackgroundColor { get; set; } = "#FFFFFF";

    private int _dpi = 203;
    /// <summary>
    /// Target printer resolution in DPI (Zebra ZD621 ships as 203 or 300). Device rendering and
    /// barcode module widths snap to whole dots at this DPI so output is crisp and scannable.
    /// </summary>
    public int Dpi
    {
        get => _dpi;
        set => _dpi = value >= 96 ? value : 203;
    }

    /// <summary>Thermal printer settings stored with the template (darkness/speed/media/offset).</summary>
    public PrinterProfile PrinterProfile { get; set; } = new();

    /// <summary>
    /// Declared field names this template uses (e.g. "itemName", "price").
    /// Elements reference these via BoundField or {{fieldName}} tokens.
    /// </summary>
    public List<string> Fields { get; set; } = new();

    /// <summary>
    /// Optional per-field metadata (required/default/format/prompt) keyed by name.
    /// Backfilled from <see cref="Fields"/> on load so older templates always have one per field.
    /// </summary>
    public List<FieldDefinition> FieldDefinitions { get; set; } = new();

    /// <summary>
    /// Maps each field name to an Excel column letter (e.g. "itemName" → "C").
    /// Set via the Manage Fields dialog so Excel Import knows where to read each field.
    /// </summary>
    public Dictionary<string, string> ExcelColumnMapping { get; set; } = new();

    /// <summary>Excel column letter whose numeric value controls how many labels to print per row.</summary>
    public string? PrintQtyColumn { get; set; }

    /// <summary>Last Excel file used with this template — pre-filled in the import dialog.</summary>
    public string? DefaultExcelPath { get; set; }

    // ── Secondary Excel join ──────────────────────────────────────────────────
    /// <summary>Optional second spreadsheet used as a lookup table.</summary>
    public string? SecondaryExcelPath { get; set; }

    /// <summary>Column letter in the PRIMARY file that is the join key (e.g. "A").</summary>
    public string? JoinPrimaryKeyColumn { get; set; }

    /// <summary>Column letter in the SECONDARY file that is the join key (e.g. "A").</summary>
    public string? JoinSecondaryKeyColumn { get; set; }

    /// <summary>Maps template field names to column letters in the secondary file.</summary>
    public Dictionary<string, string> SecondaryExcelColumnMapping { get; set; } = new();

    /// <summary>
    /// Sample values for each field, shown in preview when no live data source is active.
    /// Edited via the Manage Fields dialog.
    /// </summary>
    public Dictionary<string, string> TestData { get; set; } = new();

    /// <summary>Layers for this template. Elements can be assigned to layers for grouped visibility and conditional printing.</summary>
    public List<Layer> Layers { get; set; } = new();

    /// <summary>Computed data sources (date, time, serial) whose values are injected as fields at print/preview time.</summary>
    public List<DataSourceDefinition> DataSources { get; set; } = new();

    public List<LabelElement> Elements { get; set; } = new();

    /// <summary>Millimetres → 96-DPI logical pixels (the design-canvas coordinate space).</summary>
    public static double MmToPixels(double mm) => mm * 96.0 / 25.4;

    [JsonIgnore]
    public double WidthPx => MmToPixels(WidthMm);

    [JsonIgnore]
    public double HeightPx => MmToPixels(HeightMm);

    /// <summary>Millimetres → whole printer dots at this template's <see cref="Dpi"/> (the device coordinate space).</summary>
    public double MmToDots(double mm) => mm * Dpi / 25.4;

    [JsonIgnore]
    public int WidthDots => (int)Math.Round(WidthMm * Dpi / 25.4);

    [JsonIgnore]
    public int HeightDots => (int)Math.Round(HeightMm * Dpi / 25.4);

    /// <summary>Scale factor from 96-DPI design pixels to device dots (e.g. 203/96 ≈ 2.11).</summary>
    [JsonIgnore]
    public double DeviceScale => Dpi / 96.0;

    /// <summary>
    /// Backfills collections that were added in later schema versions so old templates load safely.
    /// </summary>
    public void OnDeserialized()
    {
        Fields                      ??= new();
        ExcelColumnMapping          ??= new();
        SecondaryExcelColumnMapping ??= new();
        TestData                    ??= new();
        Layers                      ??= new();
        DataSources                 ??= new();
        Elements                    ??= new();
        FieldDefinitions            ??= new();
        PrinterProfile              ??= new();

        if (_dpi < 96) _dpi = 203;

        // Backfill a default FieldDefinition for any declared field that doesn't have one yet,
        // so v1 templates (which only stored names) gain a schema without losing data.
        foreach (var name in Fields)
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                !FieldDefinitions.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
                FieldDefinitions.Add(new FieldDefinition { Name = name });
        }
    }
}
