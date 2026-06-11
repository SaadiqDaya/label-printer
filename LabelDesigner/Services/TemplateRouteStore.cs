using LabelDesigner.Core.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelDesigner.Services;

/// <summary>
/// Persists the data→template routing rules as TemplateRoutes.json in the TEMPLATES folder, so the
/// rules travel with the template library (point several stations at a shared TemplatesDir and they
/// all route identically). Atomic writes; enum values stored as strings so the file is hand-editable.
/// </summary>
public static class TemplateRouteStore
{
    private static readonly object _lock = new();
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    private static string FilePath => Path.Combine(AppConfig.TemplatesDir, "TemplateRoutes.json");

    public static List<TemplateRoute> Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath)) return new();
                return JsonSerializer.Deserialize<List<TemplateRoute>>(File.ReadAllText(FilePath), _json) ?? new();
            }
            catch (Exception ex)
            {
                // A malformed rules file must not take printing down — log loudly, route with no rules.
                LogService.Error($"Could not read template routing rules ({FilePath}); continuing with none.", ex);
                return new();
            }
        }
    }

    public static void Save(List<TemplateRoute> routes)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(routes, _json));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null, true);
            else File.Move(tmp, FilePath);
            LogService.Info($"Saved {routes.Count} template routing rule(s) to {FilePath}.");
        }
    }
}
