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
    /// </summary>
    public static List<ExcelRow> Load(string filePath, LabelTemplate template)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Excel file not found.", filePath);

        if (template.ExcelColumnMapping.Count == 0)
            throw new InvalidOperationException(
                "This template has no field-to-column mappings. " +
                "Open Template → Manage Fields to configure them.");

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

            // Skip completely empty rows
            if (fields.Values.All(string.IsNullOrEmpty)) continue;

            int printQty = 1;
            if (!string.IsNullOrWhiteSpace(template.PrintQtyColumn))
            {
                var raw = row.Cell(template.PrintQtyColumn.Trim().ToUpper()).GetString().Trim();
                if (int.TryParse(raw, out var q)) printQty = q;
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
