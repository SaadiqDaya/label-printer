using System.Text.Json.Serialization;

namespace LabelDesigner.Core.Models;

public class LabelTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Template";

    /// <summary>Label width in millimetres.</summary>
    public double WidthMm { get; set; } = 100;
    /// <summary>Label height in millimetres.</summary>
    public double HeightMm { get; set; } = 50;
    public string BackgroundColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// Declared field names this template uses (e.g. "itemName", "price").
    /// Elements reference these via BoundField or {{fieldName}} tokens.
    /// </summary>
    public List<string> Fields { get; set; } = new();

    /// <summary>
    /// Maps each field name to an Excel column letter (e.g. "itemName" → "C").
    /// Set via the Manage Fields dialog so Excel Import knows where to read each field.
    /// </summary>
    public Dictionary<string, string> ExcelColumnMapping { get; set; } = new();

    /// <summary>Excel column letter whose numeric value controls how many labels to print per row.</summary>
    public string? PrintQtyColumn { get; set; }

    /// <summary>Last Excel file used with this template — pre-filled in the import dialog.</summary>
    public string? DefaultExcelPath { get; set; }

    /// <summary>
    /// Sample values for each field, shown in preview when no live data source is active.
    /// Edited via the Manage Fields dialog.
    /// </summary>
    public Dictionary<string, string> TestData { get; set; } = new();

    public List<LabelElement> Elements { get; set; } = new();

    public static double MmToPixels(double mm) => mm * 96.0 / 25.4;

    [JsonIgnore]
    public double WidthPx => MmToPixels(WidthMm);

    [JsonIgnore]
    public double HeightPx => MmToPixels(HeightMm);
}
