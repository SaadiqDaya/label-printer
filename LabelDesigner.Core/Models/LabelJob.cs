namespace LabelDesigner.Core.Models;

/// <summary>
/// Sent from JaneERP (or any caller) to the Label Designer via named pipe.
/// Contains template name, field values, and print options.
/// </summary>
public class LabelJob
{
    public string TemplateName { get; set; } = "";
    public string? TemplateId { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new();
    public int Quantity { get; set; } = 1;
    public string? PrinterName { get; set; }
    public bool ShowPreview { get; set; } = true;
}
