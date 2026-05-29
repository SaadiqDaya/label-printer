using System.IO;
using System.Text.Json;

namespace LabelDesigner.Services;

/// <summary>
/// Per-machine user settings, ALWAYS stored locally in %APPDATA%\LabelDesigner\settings.json
/// (it records where the shared store is, so it can't itself live there). Editable in the Settings
/// dialog. Currently controls where the CONTINUOUS serial counter + print history live:
/// Local (this PC only) or a Shared network folder (so multiple stations share one sequence/audit).
/// </summary>
public static class UserSettings
{
    public sealed class Data
    {
        /// <summary>"Local" or "Shared".</summary>
        public string SerialStorageMode { get; set; } = "Local";
        public string SharedDataDir { get; set; } = "";
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
                return _cache ??= new Data();
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
