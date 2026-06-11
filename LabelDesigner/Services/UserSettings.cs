using System.IO;
using System.Text.Json;

namespace LabelDesigner.Services;

/// <summary>
/// Per-machine user settings, ALWAYS stored locally in %APPDATA%\LabelDesigner\settings.json
/// (it records where the shared store is, so it can't itself live there). Editable in the Settings
/// dialog. Currently controls where the CONTINUOUS serial counter + print history live:
/// Local (this PC only) or a Shared network folder (so multiple stations share one sequence/audit).
/// </summary>
/// <summary>One monitored job-drop folder (per station). The configured path is the ROOT; the
/// station creates inbox/processing/printed/failed beneath it. Any ERP that can write a CSV into
/// inbox\ can print — no bridge code required.</summary>
public sealed class WatchFolderConfig
{
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>True: valid jobs print as soon as they land. False: jobs wait in the Print Station
    /// queue until an operator releases them.</summary>
    public bool AutoPrint { get; set; }

    /// <summary>True: print the valid rows and report the bad ones (skip-with-reason).
    /// False: any bad row fails the whole job (all-or-nothing).</summary>
    public bool SkipInvalidRows { get; set; }

    /// <summary>Template used for rows with no Template column and no matching routing rule.</summary>
    public string DefaultTemplate { get; set; } = "";
}

public static class UserSettings
{
    public sealed class Data
    {
        /// <summary>"Local" or "Shared".</summary>
        public string SerialStorageMode { get; set; } = "Local";
        public string SharedDataDir { get; set; } = "";

        /// <summary>Operator name stamped on print history (Print Station). Blank → Windows username.</summary>
        public string OperatorName { get; set; } = "";

        public List<WatchFolderConfig> WatchFolders { get; set; } = new();

        /// <summary>BarTender-shim-compatible HTTP print API (localhost only), served by the Print
        /// Station: GET /health, GET /printers, POST /api/print. Off by default.</summary>
        public bool HttpApiEnabled { get; set; }
        public int HttpApiPort { get; set; } = 3100;
    }

    private static readonly object _lock = new();
    private static Data? _cache;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LabelDesigner", "settings.json");

    public static Data Current
    {
        get
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                try
                {
                    if (File.Exists(FilePath))
                        _cache = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
                }
                catch (Exception ex) { LogService.Error("Failed to read user settings.", ex); }
                _cache ??= new Data();
                _cache.WatchFolders ??= new();      // settings.json predating this field
                _cache.OperatorName ??= "";
                if (_cache.HttpApiPort <= 0 || _cache.HttpApiPort > 65535) _cache.HttpApiPort = 3100;
                return _cache;
            }
        }
    }

    public static void Save(Data data)
    {
        lock (_lock)
        {
            _cache = data;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null, true);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { LogService.Error("Failed to save user settings.", ex); }
        }
    }

    public static bool UseShared =>
        string.Equals(Current.SerialStorageMode, "Shared", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Current.SharedDataDir);

    /// <summary>The shared data dir if the user chose Shared and set a path; otherwise null.</summary>
    public static string? SharedDir => UseShared ? Current.SharedDataDir : null;
}
