using System.Text;

namespace LabelDesigner.Services;

/// <summary>Outcome of printing one parsed job — feeds the operator status line and the
/// .result.txt sidecar written next to the completed job file.</summary>
public class JobPrintResult
{
    public int RowsPrinted { get; set; }
    public int LabelsPrinted { get; set; }
    /// <summary>One line per template group: template, printer, labels/rows.</summary>
    public List<string> GroupSummaries { get; } = new();
    /// <summary>Rows that did NOT print and why (unrouted / invalid in skip mode / PrintQty 0 / unticked).</summary>
    public List<string> Skipped { get; } = new();

    public string Summary =>
        $"{LabelsPrinted} label(s) from {RowsPrinted} row(s)" +
        (Skipped.Count > 0 ? $", {Skipped.Count} row(s) skipped" : "");

    /// <summary>Full operator/audit report for the .result.txt sidecar.</summary>
    public string BuildReport(string sourceFileName, string printedBy)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Job:        {sourceFileName}");
        sb.AppendLine($"Printed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Station:    {Environment.MachineName}");
        sb.AppendLine($"Printed by: {printedBy}");
        sb.AppendLine($"Result:     {Summary}");
        if (GroupSummaries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Groups:");
            foreach (var g in GroupSummaries) sb.AppendLine("  " + g);
        }
        if (Skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Skipped rows:");
            foreach (var s in Skipped) sb.AppendLine("  " + s);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Prints a parsed job (all template groups). Strict mode validates EVERY row up front and refuses
/// the whole job on any problem (all-or-nothing); skip mode prints the clean rows and reports each
/// skipped row with its reason — explicitly, never silently.
/// </summary>
public static class JobPrinter
{
    /// <param name="rowFilter">Operator row selection (null = print every row). Unticked rows are
    /// reported as skipped so the sidecar always accounts for all rows.</param>
    /// <param name="fallbackPrinter">Used for groups whose template has no profile printer.</param>
    public static JobPrintResult Print(ParsedPrintJob job, string? fallbackPrinter, bool skipInvalidRows,
        string printedBy, string source, Func<PrintJobRow, bool>? rowFilter = null)
    {
        var result = new JobPrintResult();

        // Rows that never resolved to a template can't print anywhere.
        if (job.UnroutedRows.Count > 0)
        {
            var unrouted = job.UnroutedRows.Select(r => $"Row {r.RowNumber}: {r.Error}").ToList();
            if (!skipInvalidRows) throw new LabelValidationException(unrouted);
            result.Skipped.AddRange(unrouted);
        }

        // Pre-batch validation across ALL groups, so strict mode never half-prints a job.
        var strictErrors = new List<string>();
        var printable = new List<(PrintJobGroup Group, List<PrintJobRow> Rows)>();
        foreach (var group in job.Groups)
        {
            var rows = new List<PrintJobRow>();
            foreach (var row in group.Rows)
            {
                if (rowFilter != null && !rowFilter(row))
                {
                    result.Skipped.Add($"Row {row.RowNumber}: not selected by operator.");
                    continue;
                }
                if (row.Qty <= 0)
                {
                    result.Skipped.Add($"Row {row.RowNumber}: PrintQty is 0.");
                    continue;
                }
                var errors = PrintService.Validate(group.Template, row.Fields);
                if (errors.Count > 0)
                {
                    var msg = $"Row {row.RowNumber} ('{group.Template.Name}'): {string.Join("; ", errors)}";
                    row.Error = string.Join("; ", errors);
                    if (skipInvalidRows) result.Skipped.Add(msg);
                    else strictErrors.Add(msg);
                    continue;
                }
                rows.Add(row);
            }
            if (rows.Count > 0) printable.Add((group, rows));
        }
        if (strictErrors.Count > 0) throw new LabelValidationException(strictErrors);

        // SHEET JOBS: when every template in the job has a page layout (Avery sheets, menu cards),
        // labels must FLOW ACROSS SHEET CELLS in the file's row order — possibly mixing templates
        // on one page (that's how flavor-card menus work). One printer for the whole job.
        // PrintRows enforces grid compatibility and throws loudly on a mismatch (config error,
        // not a row error — skip mode doesn't apply).
        if (printable.Count > 0 &&
            printable.All(p => p.Group.Template.Page != null &&
                               p.Group.Template.PrinterProfile.OutputMode == Core.Models.PrintBackend.Gdi))
        {
            var ordered = printable
                .SelectMany(p => p.Rows.Select(r => (p.Group.Template, Row: r)))
                .OrderBy(x => x.Row.RowNumber)
                .ToList();
            var batch = ordered.Select(x => (x.Template, x.Row.Fields, x.Row.Qty)).ToList();
            var sheetPrinter = printable
                .Select(p => p.Group.Template.PrinterProfile.PrinterName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? fallbackPrinter;

            int labels = PrintService.PrintRows(batch, sheetPrinter,
                allowFallbackPrinter: false, source: source, printedBy: printedBy);

            result.RowsPrinted = batch.Count;
            result.LabelsPrinted = labels;
            result.GroupSummaries.Add(
                $"Sheet job → {(string.IsNullOrWhiteSpace(sheetPrinter) ? "(default printer)" : sheetPrinter)}: " +
                $"{labels} label(s) from {batch.Count} row(s) across {printable.Count} template(s)");
            return result;
        }

        // Print group by group; each group routes to its template's profile printer when set.
        foreach (var (group, rows) in printable)
        {
            var profilePrinter = group.Template.PrinterProfile.PrinterName;
            var printer = string.IsNullOrWhiteSpace(profilePrinter) ? fallbackPrinter : profilePrinter;

            int groupLabels = 0;
            foreach (var row in rows)
            {
                PrintService.Print(group.Template, row.Fields, printer, copies: row.Qty,
                    allowFallbackPrinter: false, validate: false, source: source, printedBy: printedBy);
                groupLabels += row.Qty;
                result.RowsPrinted++;
            }
            result.LabelsPrinted += groupLabels;
            result.GroupSummaries.Add(
                $"'{group.Template.Name}' → {(string.IsNullOrWhiteSpace(printer) ? "(default printer)" : printer)}: " +
                $"{groupLabels} label(s) from {rows.Count} row(s)");
        }

        return result;
    }
}
