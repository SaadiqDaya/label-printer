using System.Text.Json;
using System.Text.Json.Serialization;
using LabelDesigner.Core.Models;

namespace LabelDesigner.Core.Services;

public class TemplateService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directory;

    public TemplateService(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    private static readonly object _ioLock = new();

    public void Save(LabelTemplate template, string filePath)
    {
        var json = JsonSerializer.Serialize(template, JsonOptions);
        lock (_ioLock)
        {
            // Atomic write: stage to *.tmp then replace, so a crash mid-write can't corrupt the live file.
            var dir  = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp  = filePath + ".tmp";
            File.WriteAllText(tmp, json);

            if (File.Exists(filePath))
                File.Replace(tmp, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tmp, filePath);
        }
    }

    public LabelTemplate? Load(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        string json;
        lock (_ioLock) { json = File.ReadAllText(filePath); }
        return JsonSerializer.Deserialize<LabelTemplate>(json, JsonOptions);
    }

    public string GetDefaultPath(LabelTemplate template) =>
        Path.Combine(_directory, SanitizeFileName(template.Name) + ".lbl");

    public IEnumerable<string> GetTemplatePaths() =>
        Directory.Exists(_directory)
            ? Directory.GetFiles(_directory, "*.lbl")
            : Enumerable.Empty<string>();

    public IEnumerable<LabelTemplate> LoadAll()
    {
        foreach (var path in GetTemplatePaths())
        {
            var t = Load(path);
            if (t != null) yield return t;
        }
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
