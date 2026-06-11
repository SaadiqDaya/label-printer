using System.IO;
using System.Text;
using System.Text.Json;

namespace LabelDesigner.Services;

/// <summary>How to regenerate one serial source's sequence for a faithful reprint.</summary>
public class SerialPlanItem
{
    public string Name { get; set; } = "";
    public long Base { get; set; }
    public int Increment { get; set; } = 1;
    public string Format { get; set; } = "";
    // Alphanumeric/prefix-suffix params (default values reproduce the old decimal behaviour).
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
    public int Radix { get; set; } = 10;
    public int PadWidth { get; set; }
}

/// <summary>One recorded print run (a batch from one Print call). Stores enough to reprint the
/// EXACT same labels — including the serial sequence and the date/values used at the time.</summary>
public class PrintHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TimestampIso { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string? TemplateId { get; set; }
    public string? Printer { get; set; }

    /// <summary>Labels requested for this run.</summary>
    public int Qty { get; set; }
    /// <summary>Labels actually sent to the spooler (≤ Qty when a batch failed partway).</summary>
    public int ActualPrinted { get; set; }

    /// <summary>Manual / PrintStation / IPC / WatchFolder / Reprint.</summary>
    public string Source { get; set; } = "Manual";

    /// <summary>Who printed it: the operator name from the Print Station, else the Windows username.
    /// Null on entries recorded before this field existed.</summary>
    public string? PrintedBy { get; set; }

    public string? AppVersion { get; set; }

    /// <summary>Caller-supplied field values (Excel/operator/IPC).</summary>
    public Dictionary<string, string> Fields { get; set; } = new();

    /// <summary>Non-serial data-source values used at print time (date/time/fixed) — constant across the batch.</summary>
    public Dictionary<string, string> ResolvedConstants { get; set; } = new();

    /// <summary>Serial sources + the base/increment/format used, so a reprint regenerates identical IDs.</summary>
    public List<SerialPlanItem> SerialPlan { get; set; } = new();

    public string Display =>
        $"{TimestampIso}  {TemplateName}  ×{(ActualPrinted > 0 ? ActualPrinted : Qty)}  [{Source}{(string.IsNullOrWhiteSpace(PrintedBy) ? "" : " · " + PrintedBy)}]  → {Printer}";
}

/// <summary>
/// Persistent print-history / reprint store. JSON under <see cref="AppConfig.DataDir"/> (share that
/// path to get a shop-wide audit trail). Writes are exclusive-file-locked so concurrent stations
/// don't corrupt a shared file. Recorded once per Print() batch at the single print choke point.
/// </summary>
public static class PrintHistoryService
{
    private static readonly object _lock = new();
    private static string File_ => Path.Combine(AppConfig.DataDir, "history.json");
    private const int MaxEntries = 10000;

    /// <summary>Most recent entries first.</summary>
    public static IReadOnlyList<PrintHistoryEntry> Recent(int max = 200)
    {
        lock (_lock)
        {
            var list = Read();
            return list.AsEnumerable().Reverse().Take(max).ToList();
        }
    }

    public static void Record(PrintHistoryEntry entry)
    {
        entry.AppVersion ??= AppConfig.AppVersion;
        lock (_lock)
        {
            WithExclusiveFile(list =>
            {
                list.Add(entry);
                if (list.Count > MaxEntries) list.RemoveRange(0, list.Count - MaxEntries);
                return true;
            });
        }
    }

    /// <summary>Exports the full history to CSV for reconciliation/audit. Returns rows written.</summary>
    public static int ExportCsv(string path)
    {
        lock (_lock)
        {
            var list = Read();
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,Template,Source,PrintedBy,Printer,RequestedQty,ActualPrinted,AppVersion,Fields");
            foreach (var e in list)
            {
                var fields = string.Join("; ", e.Fields.Select(kv => $"{kv.Key}={kv.Value}"));
                sb.AppendLine(string.Join(",",
                    Csv(e.TimestampIso), Csv(e.TemplateName), Csv(e.Source), Csv(e.PrintedBy ?? ""), Csv(e.Printer ?? ""),
                    e.Qty, e.ActualPrinted, Csv(e.AppVersion ?? ""), Csv(fields)));
            }
            File.WriteAllText(path, sb.ToString());
            return list.Count;
        }
    }

    private static string Csv(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static void WithExclusiveFile(Func<List<PrintHistoryEntry>, bool> mutate)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var fs = new FileStream(File_, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    var list = ReadList(fs);
                    if (mutate(list))
                    {
                        fs.Position = 0;
                        fs.SetLength(0);
                        using var sw = new StreamWriter(fs);
                        sw.Write(JsonSerializer.Serialize(list));
                        sw.Flush();
                    }
                    return;
                }
                catch (IOException) when (attempt < 100)
                {
                    Thread.Sleep(15);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Print history store access failed.", ex);
        }
    }

    private static List<PrintHistoryEntry> Read()
    {
        List<PrintHistoryEntry>? result = null;
        WithExclusiveFile(list => { result = list; return false; });
        return result ?? new();
    }

    private static List<PrintHistoryEntry> ReadList(FileStream fs)
    {
        try
        {
            fs.Position = 0;
            using var sr = new StreamReader(fs, leaveOpen: true);
            var json = sr.ReadToEnd();
            if (string.IsNullOrWhiteSpace(json)) return new();
            return JsonSerializer.Deserialize<List<PrintHistoryEntry>>(json) ?? new();
        }
        catch { return new(); }
    }
}
