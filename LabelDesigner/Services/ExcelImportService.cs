using ClosedXML.Excel;
using LabelDesigner.Core.Models;
using System.IO;

namespace LabelDesigner.Services;

/// <summary>A single row read from Excel, with field values keyed by template field name.</summary>
public record ExcelRow(Dictionary<string, string> Fields, int PrintQty);

public static class ExcelImportService
{
    /// <summary>
    /// Load rows from <paramref name="filePath"/> using the template's ExcelColumnMapping.
    /// Skips rows where every mapped field is empty.
    /// PrintQty comes from <see cref="LabelTemplate.PrintQtyColumn"/> (defaults to 1 if not set or not numeric).
    ///
    /// If the template has a SecondaryExcelPath + join keys configured, fields from the secondary
    /// worksheet are merged in for each row (keyed by the join column values).
    /// </summary>
    public static List<ExcelRow> Load(string filePath, LabelTemplate template)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Excel file not found.", filePath);

        if (template.ExcelColumnMapping.Count == 0)
            throw new InvalidOperationException(
                "This template has no field-to-column mappings. " +
                "Open Template → Manage Fields to configure them.");

        // Pre-build secondary lookup if configured
        Dictionary<string, Dictionary<string, string>>? secondaryLookup = null;
        if (!string.IsNullOrWhiteSpace(template.SecondaryExcelPath) &&
            File.Exists(template.SecondaryExcelPath) &&
            !string.IsNullOrWhiteSpace(template.JoinSecondaryKeyColumn) &&
            template.SecondaryExcelColumnMapping.Count > 0)
        {
            secondaryLookup = BuildSecondaryLookup(
                template.SecondaryExcelPath,
                template.JoinSecondaryKeyColumn,
                template.SecondaryExcelColumnMapping);
        }

        var results = new List<ExcelRow>();

        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet(1);
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (fieldName, colLetter) in template.ExcelColumnMapping)
            {
                if (string.IsNullOrWhiteSpace(colLetter)) continue;
                fields[fieldName] = row.Cell(colLetter.Trim().ToUpper()).GetString().Trim();
            }

            // Merge secondary fields when join is configured
            if (secondaryLookup != null && !string.IsNullOrWhiteSpace(template.JoinPrimaryKeyColumn))
            {
                var keyValue = row.Cell(template.JoinPrimaryKeyColumn.Trim().ToUpper()).GetString().Trim();
                if (!string.IsNullOrEmpty(keyValue) &&
                    secondaryLookup.TryGetValue(keyValue, out var secFields))
                {
                    foreach (var kv in secFields)
                        fields[kv.Key] = kv.Value; // secondary supplements/overrides
                }
            }

            // Skip completely empty rows
            if (fields.Values.All(string.IsNullOrEmpty)) continue;

            // PrintQty rules:
            //   - No PrintQtyColumn configured → default to 1 (print every row once).
            //   - PrintQtyColumn configured → use the row's value verbatim. Empty / non-numeric / 0
            //     means "skip this row" (qty 0); negatives clamp to 0.
            int printQty;
            if (string.IsNullOrWhiteSpace(template.PrintQtyColumn))
            {
                printQty = 1;
            }
            else
            {
                // PrintQtyColumn can be a template field name (e.g. "Qty") or a raw Excel column letter (e.g. "C")
                string raw;
                if (fields.TryGetValue(template.PrintQtyColumn, out var fieldVal))
                    raw = fieldVal; // matched a mapped field name — preferred
                else
                    raw = row.Cell(template.PrintQtyColumn.Trim().ToUpper()).GetString().Trim(); // fallback: treat as column letter

                printQty = int.TryParse(raw, out var q) ? Math.Max(0, q) : 0;
            }

            results.Add(new ExcelRow(fields, printQty));
        }

        return results;
    }

    /// <summary>
    /// Returns column-header pairs (ColumnLetter → HeaderText) from the first row of the
    /// first worksheet, so the Manage Fields dialog can show what columns are available.
    /// </summary>
    public static List<(string Letter, string Header)> ReadHeaders(string filePath)
    {
        if (!File.Exists(filePath)) return new();

        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet(1);
        var headerRow = ws.Row(1);
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        var result = new List<(string, string)>();
        for (int c = 1; c <= lastCol; c++)
        {
            var letter = GetColumnLetter(c);
            var header = headerRow.Cell(c).GetString().Trim();
            if (!string.IsNullOrEmpty(header))
                result.Add((letter, header));
        }
        return result;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads all rows from the secondary file and indexes them by the value in
    /// <paramref name="keyColumn"/> so primary rows can do an O(1) lookup.
    /// When duplicate key values exist, the last row wins.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> BuildSecondaryLookup(
        string filePath, string keyColumn, Dictionary<string, string> columnMapping)
    {
        var lookup = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet(1);
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var keyValue = row.Cell(keyColumn.Trim().ToUpper()).GetString().Trim();
            if (string.IsNullOrEmpty(keyValue)) continue;

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fieldName, colLetter) in columnMapping)
            {
                if (string.IsNullOrWhiteSpace(colLetter)) continue;
                fields[fieldName] = row.Cell(colLetter.Trim().ToUpper()).GetString().Trim();
            }

            lookup[keyValue] = fields;
        }

        return lookup;
    }

    private static string GetColumnLetter(int col)
    {
        var result = "";
        while (col > 0)
        {
            col--;
            result = (char)('A' + col % 26) + result;
            col /= 26;
        }
        return result;
    }
}
