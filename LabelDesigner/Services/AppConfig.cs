using System.IO;
using System.Reflection;
using System.Text.Json;

namespace LabelDesigner.Services;

/// <summary>
/// Central configuration for file locations and IPC.
///
/// TemplatesDir / PipeName / payload come from: environment variable → appsettings.json (next to the
/// exe) → built-in default.
///
/// DataDir (serial counters, print history, logs) is resolved dynamically each call so the in-app
/// Settings dialog takes effect immediately: env/appsettings (admin lock) → the user's "Shared
/// folder" choice (<see cref="UserSettings"/>) → local %APPDATA%.
/// </summary>
public static class AppConfig
{
    private static readonly Settings _s = Load();

    public static string  TemplatesDir    => _s.TemplatesDir;
    public static string  PipeName        => _s.PipeName;
    public static int     MaxPayloadBytes => _s.MaxPayloadBytes;
    public static string? DefaultPrinter  => _s.DefaultPrinter;

    public static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    // Admin-locked data dir from env/appsettings ("" = not set, so the user's choice applies).
    private static readonly string _adminDataDir =
        Env("LABELDESIGNER_DATA_DIR") ?? NullIfBlank(_s.DataDir) ?? "";

    /// <summary>True when an admin pinned DataDir via env/appsettings — the Settings dialog then shows it read-only.</summary>
    public static bool IsDataDirAdminLocked => !string.IsNullOrWhiteSpace(_adminDataDir);

    private static string DefaultDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LabelDesigner");

    /// <summary>Where counters / history / logs live. Re-evaluated each call (see class summary).</summary>
    public static string DataDir
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_adminDataDir)) return _adminDataDir; // admin lock wins
            if (UserSettings.SharedDir is string shared) return shared;          // user chose Shared
            return DefaultDataDir;                                               // local default
        }
    }

    public sealed class Settings
    {
        public string TemplatesDir { get; set; } = "";
        public string DataDir { get; set; } = "";
        public string PipeName { get; set; } = "LabelDesigner";
        public int MaxPayloadBytes { get; set; } = 64 * 1024;
        public string? DefaultPrinter { get; set; }
    }

    private static Settings Load()
    {
        var s = new Settings();
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                var fromFile = JsonSerializer.Deserialize<Settings>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (fromFile != null) s = fromFile;
            }
        }
        catch { /* malformed config → fall back to defaults below */ }

        s.TemplatesDir = Env("LABELDESIGNER_TEMPLATES_DIR") ?? NullIfBlank(s.TemplatesDir)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LabelDesigner", "Templates");

        // NOTE: s.DataDir is left as the raw env/appsettings value (possibly blank); the DataDir
        // property layers the user choice + default on top of it.

        var pipe = Env("LABELDESIGNER_PIPE");
        if (!string.IsNullOrWhiteSpace(pipe)) s.PipeName = pipe!;

        return s;
    }

    private static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
