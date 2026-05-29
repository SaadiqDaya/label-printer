using LabelDesigner.Core.Models;
using System.IO;
using System.Text;

namespace LabelDesigner.Services;

/// <summary>
/// Reads CSV/TSV files into the same <see cref="ExcelRow"/> shape as <see cref="ExcelImportService"/>,
/// using the template's column-LETTER mapping (A = 1st column, B = 2nd, …) so Excel and CSV templates
/// are interchangeable and everything downstream (mapping, PrintQty, two-file join, ValidateBatch,
/// print-all) works unchanged. RFC-4180: quoted fields, embedded commas/newlines, doubled quotes.
/// </summary>
public static class CsvImportService
{
    private static readonly StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    public static List<ExcelRow> Load(string filePath, LabelTemplate template)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Data file not found.", filePath);
        if (template.ExcelColumnMapping.Count == 0)
            throw new InvalidOperationException(
                "This template has no field-to-column mappings. Open Template → Manage Fields to configure them.");

        var rows = Parse(File.ReadAllText(filePath), Delimiter(filePath));
        var results = new List<ExcelRow>();
        if (rows.Count < 2) return results; // header + at least one data row

        // Secondary join only when the secondary file is also CSV/TSV (mixed Excel+CSV join is rare).
        Dictionary<string, Dictionary<string, string>>? secondary = null;
        if (!string.IsNullOrWhiteSpace(template.SecondaryExcelPath) &&
            File.Exists(template.SecondaryExcelPath) && IsCsv(template.SecondaryExcelPath) &&
            !string.IsNullOrWhiteSpace(template.JoinSecondaryKeyColumn) &&
            template.SecondaryExcelColumnMapping.Count > 0)
            secondary = BuildSecondaryLookup(template.SecondaryExcelPath, template.JoinSecondaryKeyColumn,
                                             template.SecondaryExcelColumnMapping);

        for (int r = 1; r < rows.Count; r++)
        {
            var cells = rows[r];
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fieldName, col) in template.ExcelColumnMapping)
            {
                if (string.IsNullOrWhiteSpace(col)) continue;
                int idx = LetterToIndex(col);
                fields[fieldName] = (idx >= 0 && idx < cells.Length) ? cells[idx].Trim() : "";
            }

            if (secondary != null && !string.IsNullOrWhiteSpace(template.JoinPrimaryKeyColumn))
            {
                int keyIdx = LetterToIndex(template.JoinPrimaryKeyColumn);
                var keyVal = (keyIdx >= 0 && keyIdx < cells.Length) ? cells[keyIdx].Trim() : "";
                if (!string.IsNullOrEmpty(keyVal) && secondary.TryGetValue(keyVal, out var sec))
                    foreach (var kv in sec) fields[kv.Key] = kv.Value;
            }

            if (fields.Values.All(string.IsNullOrEmpty)) continue;

            int printQty;
            if (string.IsNullOrWhiteSpace(template.PrintQtyColumn)) printQty = 1;
            else
            {
                string raw;
                if (fields.TryGetValue(template.PrintQtyColumn, out var fv)) raw = fv;
                else { int qi = LetterToIndex(template.PrintQtyColumn); raw = (qi >= 0 && qi < cells.Length) ? cells[qi].Trim() : ""; }
                printQty = int.TryParse(raw, out var q) ? Math.Max(0, q) : 0;
            }

            results.Add(new ExcelRow(fields, printQty));
        }
        return results;
    }

    /// <summary>Column-letter → header from the first row, so the Manage Fields dropdown is identical to Excel.</summary>
    public static List<(string Letter, string Header)> ReadHeaders(string filePath)
    {
        if (!File.Exists(filePath)) return new();
        var rows = Parse(File.ReadAllText(filePath), Delimiter(filePath));
        var result = new List<(string, string)>();
        if (rows.Count == 0) return result;
        var header = rows[0];
        for (int c = 0; c < header.Length; c++)
        {
            var h = header[c].Trim();
            if (!string.IsNullOrEmpty(h)) result.Add((IndexToLetter(c), h));
        }
        return result;
    }

    public static bool IsCsv(string path) => path.EndsWith(".csv", OIC) || path.EndsWith(".tsv", OIC);

    private static char Delimiter(string path) => path.EndsWith(".tsv", OIC) ? '\t' : ',';

    private static Dictionary<string, Dictionary<string, string>> BuildSecondaryLookup(
        string path, string keyCol, Dictionary<string, string> mapping)
    {
        var lookup = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var rows = Parse(File.ReadAllText(path), Delimiter(path));
        int keyIdx = LetterToIndex(keyCol);
        for (int r = 1; r < rows.Count; r++)
        {
            var cells = rows[r];
            var keyVal = (keyIdx >= 0 && keyIdx < cells.Length) ? cells[keyIdx].Trim() : "";
            if (string.IsNullOrEmpty(keyVal)) continue;
            var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fn, col) in mapping)
            {
                if (string.IsNullOrWhiteSpace(col)) continue;
                int i = LetterToIndex(col);
                f[fn] = (i >= 0 && i < cells.Length) ? cells[i].Trim() : "";
            }
            lookup[keyVal] = f;
        }
        return lookup;
    }

    /// <summary>RFC-4180 parse into rows of fields.</summary>
    private static List<string[]> Parse(string text, char delim)
    {
        var rows = new List<string[]>();
        var field = new StringBuilder();
        var current = new List<string>();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == delim) { current.Add(field.ToString()); field.Clear(); }
                else if (c == '\r') { /* handled by \n */ }
                else if (c == '\n') { current.Add(field.ToString()); field.Clear(); rows.Add(current.ToArray()); current = new(); }
                else field.Append(c);
            }
        }
        if (field.Length > 0 || current.Count > 0) { current.Add(field.ToString()); rows.Add(current.ToArray()); }

        // Drop fully-blank lines.
        return rows.Where(r => !(r.Length == 1 && r[0].Length == 0)).ToList();
    }

    private static int LetterToIndex(string letter)
    {
        letter = letter.Trim().ToUpperInvariant();
        int num = 0;
        foreach (char c in letter) { if (c < 'A' || c > 'Z') return -1; num = num * 26 + (c - 'A' + 1); }
        return num - 1;
    }

    private static string IndexToLetter(int index)
    {
        index++;
        var s = "";
        while (index > 0) { index--; s = (char)('A' + index % 26) + s; index /= 26; }
        return s;
    }
}

/// <summary>Routes data loading to the right importer by file extension (.csv/.tsv → CSV, else Excel),
/// so templates can use either format with the same column-letter mapping.</summary>
public static class DataImporter
{
    public static List<ExcelRow> Load(string path, LabelTemplate template) =>
        CsvImportService.IsCsv(path) ? CsvImportService.Load(path, template) : ExcelImportService.Load(path, template);

    public static List<(string Letter, string Header)> ReadHeaders(string path) =>
        CsvImportService.IsCsv(path) ? CsvImportService.ReadHeaders(path) : ExcelImportService.ReadHeaders(path);
}
