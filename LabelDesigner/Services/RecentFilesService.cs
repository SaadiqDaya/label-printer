using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LabelDesigner.Services;

public static class RecentFilesService
{
    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LabelDesigner", "recent_files.json");

    private const int MaxFiles = 10;

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new();
            var json = File.ReadAllText(_settingsPath);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            // Filter out files that no longer exist
            return list?.Where(File.Exists).ToList() ?? new();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[RecentFilesService] Load failed: " + ex.Message);
            return new();
        }
    }

    public static void Push(string filePath)
    {
        try
        {
            var list = Load();
            list.RemoveAll(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, filePath);
            if (list.Count > MaxFiles) list = list.Take(MaxFiles).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(list));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[RecentFilesService] Push failed: " + ex.Message);
        }
    }
}
