namespace LabelDesigner.Core.Models;

/// <summary>
/// Sent from JaneERP (or any caller) to the Label Designer via named pipe.
/// Contains template name, field values, and print options.
/// </summary>
public class LabelJob
{
    /// <summary>Optional caller-supplied id, echoed back in the response so JaneERP can correlate the outcome.</summary>
    public string? JobId { get; set; }
    public string TemplateName { get; set; } = "";
    public string? TemplateId { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new();
    public int Quantity { get; set; } = 1;
    public string? PrinterName { get; set; }
    public bool ShowPreview { get; set; } = true;
}

/// <summary>Outcome of a LabelJob, logged per job and written back to duplex callers.</summary>
public class LabelJobResponse
{
    public string? JobId { get; set; }
    /// <summary>accepted | printed | rejected | error</summary>
    public string Status { get; set; } = "accepted";
    public string? Message { get; set; }
}
