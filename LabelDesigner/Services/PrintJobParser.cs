using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;

namespace LabelDesigner.Services;

/// <summary>One data row of a print job. Qty 0 means "listed but skipped" (matches PrintQty=0 semantics).</summary>
public class PrintJobRow
{
    /// <summary>1-based data-row number in the source file (header excluded), for operator-readable reports.</summary>
    public int RowNumber { get; init; }
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int Qty { get; set; } = 1;
    /// <summary>Routing/validation problem for this row (null = OK so far).</summary>
    public string? Error { get; set; }
}

/// <summary>All of a job's rows that resolved to the same template.</summary>
public class PrintJobGroup
{
    public LabelTemplate Template { get; init; } = null!;
    public List<PrintJobRow> Rows { get; } = new();
}

/// <summary>A parsed job file: rows grouped per resolved template + the rows that could not be routed.</summary>
public class ParsedPrintJob
{
    public List<PrintJobGroup> Groups { get; } = new();
    /// <summary>Rows that couldn't be routed to a template (missing/unknown name). Never printed.</summary>
    public List<PrintJobRow> UnroutedRows { get; } = new();
    public int TotalRows { get; set; }
    public int TotalLabels => Groups.Sum(g => g.Rows.Sum(r => r.Qty));
}

/// <summary>
/// Parses watch-folder job rows (header-name dicts from <see cref="CsvImportService.LoadGeneric"/>)
/// into per-template groups. Template per row: "Template" column → routing rules → folder default.
/// Quantity per row: "PrintQty" / "Qty" / "Copies" column (0 = skip, blank = 1).
/// </summary>
public static class PrintJobParser
{
    private static readonly string[] QtyColumns = { "PrintQty", "Qty", "Copies" };

    public static ParsedPrintJob Parse(
        IReadOnlyList<Dictionary<string, string>> rows,
        Func<string, LabelTemplate?> templateLookup,
        IReadOnlyList<TemplateRoute> routes,
        string? defaultTemplate)
    {
        var job = new ParsedPrintJob { TotalRows = rows.Count };
        // Cache lookups so a 500-row job doesn't re-load the same template file 500 times.
        var cache = new Dictionary<string, LabelTemplate?>(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, PrintJobGroup>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = new PrintJobRow { RowNumber = i + 1, Fields = rows[i], Qty = ReadQty(rows[i]) };

            var name = TemplateRouter.ResolveName(rows[i], routes, defaultTemplate);
            if (name == null)
            {
                row.Error = "No template: row has no Template column value, no routing rule matches, and the folder has no default template.";
                job.UnroutedRows.Add(row);
                continue;
            }

            if (!cache.TryGetValue(name, out var template))
                cache[name] = template = templateLookup(name);
            if (template == null)
            {
                row.Error = $"Unknown template \"{name}\" — no template with that name exists.";
                job.UnroutedRows.Add(row);
                continue;
            }

            if (!groups.TryGetValue(name, out var group))
                groups[name] = group = new PrintJobGroup { Template = template };
            group.Rows.Add(row);
        }

        job.Groups.AddRange(groups.Values);
        return job;
    }

    /// <summary>Row quantity: first present qty column wins; blank/missing = 1; non-numeric or negative = 0 (skip, never guess).</summary>
    public static int ReadQty(IReadOnlyDictionary<string, string> fields)
    {
        foreach (var col in QtyColumns)
        {
            if (!fields.TryGetValue(col, out var raw)) continue;
            if (string.IsNullOrWhiteSpace(raw)) return 1;
            return int.TryParse(raw.Trim(), out var q) && q >= 0 ? q : 0;
        }
        return 1;
    }
}
