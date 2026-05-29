using System.IO;
using System.Text.Json;
using LabelDesigner.Core.Models;

namespace LabelDesigner.Services;

/// <summary>
/// Persists serial/counter values, keyed by template Id + data-source Id, in a JSON file under
/// <see cref="AppConfig.DataDir"/> (point that at a shared UNC path to share one sequence across
/// stations). Correctness rules the audit demanded:
///
///  • <see cref="Reserve"/> does an EXCLUSIVE, file-locked read-modify-write (FileShare.None with
///    retry), so two stations / two app instances on a shared counters.json cannot mint the same
///    serial — the second waits for the first to finish. No in-memory cache (a cache would hide
///    another machine's writes).
///  • Serials are advanced BEFORE the labels print (reserve-then-print). A crash/jam mid-batch
///    therefore leaves a GAP in the sequence (safe) rather than reusing already-printed numbers
///    (unsafe duplicates).
/// </summary>
public static class SerialCounterStore
{
    private static readonly object _lock = new();
    private static string File_ => Path.Combine(AppConfig.DataDir, "counters.json");

    private static string Key(LabelTemplate t, DataSourceDefinition ds) => $"{t.Id}:{ds.Id}";

    /// <summary>
    /// Atomically reserves <paramref name="count"/> serial values for every Serial source in the
    /// template and returns each source's starting base. Label i (0-based) uses base + i×Increment.
    /// The persisted counter is advanced immediately so the next batch continues the sequence.
    /// </summary>
    public static IReadOnlyDictionary<Guid, long> Reserve(LabelTemplate template, int count)
    {
        var reserved = new Dictionary<Guid, long>();
        if (count <= 0) return reserved;
        var serials = template.DataSources.Where(d => d.Type == DataSourceType.Serial).ToList();
        if (serials.Count == 0) return reserved;

        // Reset-per-batch sources start at SerialStart every job — no persistence, no file/network touch.
        foreach (var ds in serials.Where(d => d.SerialMode == SerialMode.ResetPerBatch))
            reserved[ds.Id] = ds.SerialStart;

        var continuous = serials.Where(d => d.SerialMode == SerialMode.Continuous).ToList();
        if (continuous.Count == 0) return reserved;   // nothing persistent → never opens the store

        bool ok;
        lock (_lock)
        {
            ok = WithExclusiveFile(map =>
            {
                foreach (var ds in continuous)
                {
                    var key = Key(template, ds);
                    long baseVal = map.TryGetValue(key, out var v) ? v : ds.SerialStart;
                    reserved[ds.Id] = baseVal;
                    map[key] = baseVal + (long)count * Math.Max(1, ds.Increment);
                }
                return true; // write
            });
        }

        // Fail loud: if the (possibly shared) store was unreachable, do NOT silently fall back to the
        // start value — that would reuse already-printed serials. Abort so the caller can surface it.
        if (!ok)
            throw new SerialStoreUnavailableException(File_);

        return reserved;
    }

    /// <summary>Current base for a source WITHOUT advancing — for preview/validation only.</summary>
    public static long GetBase(LabelTemplate template, DataSourceDefinition ds)
    {
        // Reset-per-batch always previews from its start; never reads the persisted store.
        if (ds.SerialMode == SerialMode.ResetPerBatch) return ds.SerialStart;

        lock (_lock)
        {
            long result = ds.SerialStart;
            WithExclusiveFile(map =>
            {
                if (map.TryGetValue(Key(template, ds), out var v)) result = v;
                return false; // read-only
            });
            return result;
        }
    }

    /// <summary>Reset a template's serial counters back to their SerialStart.</summary>
    public static void Reset(LabelTemplate template)
    {
        lock (_lock)
        {
            WithExclusiveFile(map =>
            {
                bool changed = false;
                foreach (var ds in template.DataSources.Where(d => d.Type == DataSourceType.Serial))
                    changed |= map.Remove(Key(template, ds));
                return changed;
            });
        }
    }

    /// <summary>
    /// Opens counters.json exclusively (FileShare.None) with brief retry so a shared-UNC file is safe
    /// across machines, hands the parsed map to <paramref name="mutate"/>, and writes it back if the
    /// callback returns true. Never throws to the caller — a counter failure must not stop printing,
    /// but it IS logged.
    /// </summary>
    /// <summary>Returns true if the file op completed; false if the store was unreachable/unwritable
    /// (shared folder offline, no permission, etc.) after exhausting retries.</summary>
    private static bool WithExclusiveFile(Func<Dictionary<string, long>, bool> mutate)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var fs = new FileStream(File_, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    var map = ReadMap(fs);
                    if (mutate(map))
                    {
                        fs.Position = 0;
                        fs.SetLength(0);
                        using var sw = new StreamWriter(fs);
                        sw.Write(JsonSerializer.Serialize(map));
                        sw.Flush();
                    }
                    return true;
                }
                catch (IOException) when (attempt < 100)
                {
                    Thread.Sleep(15); // another station/instance holds the file — wait and retry
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Serial counter store access failed.", ex);
            return false;
        }
    }

    private static Dictionary<string, long> ReadMap(FileStream fs)
    {
        try
        {
            fs.Position = 0;
            using var sr = new StreamReader(fs, leaveOpen: true);
            var json = sr.ReadToEnd();
            if (string.IsNullOrWhiteSpace(json)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? new();
        }
        catch { return new(); }
    }
}
