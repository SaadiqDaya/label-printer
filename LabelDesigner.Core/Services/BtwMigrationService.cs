using System.Text.Json;
using LabelDesigner.Core.Models;

namespace LabelDesigner.Core.Services;

/// <summary>
/// Persistent tracker for the BarTender-library migration, stored as BtwMigration.json in the
/// templates folder (shared, so every station sees the same progress). Atomic writes like
/// <see cref="TemplateService"/>.
/// </summary>
public class BtwMigrationStore
{
    public const string FileName = "BtwMigration.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>Folder last scanned, so reopening the dialog resumes where the user left off.</summary>
    public string LastFolder { get; set; } = "";

    public List<BtwMigrationEntry> Entries { get; set; } = new();

    public static BtwMigrationStore Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new BtwMigrationStore();
        var store = JsonSerializer.Deserialize<BtwMigrationStore>(File.ReadAllText(path), JsonOptions)
                    ?? new BtwMigrationStore();
        store.Entries ??= new();
        return store;
    }

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var tmp  = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else File.Move(tmp, path);
    }

    /// <summary>
    /// Folds a fresh folder scan into the tracker: new files are added as Pending (or Unreadable),
    /// existing entries keep their Status/Target/Notes but refresh header metadata, and entries
    /// under <paramref name="scanRoot"/> whose file has vanished are flagged
    /// <see cref="BtwMigrationEntry.SourceMissing"/> (never silently dropped — they may be on a
    /// disconnected share).
    /// </summary>
    public void MergeScan(string scanRoot, IReadOnlyList<BtwMigrationEntry> scanned)
    {
        var byPath = Entries.ToDictionary(e => e.SourcePath, StringComparer.OrdinalIgnoreCase);
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in scanned)
        {
            seen.Add(s.SourcePath);
            if (byPath.TryGetValue(s.SourcePath, out var existing))
            {
                existing.SourceMissing = false;
                if (s.Status != BtwMigrationStatus.Unreadable)
                {
                    existing.Title    = s.Title;
                    existing.WidthMm  = s.WidthMm;
                    existing.HeightMm = s.HeightMm;
                    existing.Printer  = s.Printer;
                    // A previously unreadable file that now parses becomes workable again.
                    if (existing.Status == BtwMigrationStatus.Unreadable)
                        existing.Status = BtwMigrationStatus.Pending;
                }
            }
            else
            {
                Entries.Add(s);
            }
        }

        foreach (var e in Entries)
        {
            if (!seen.Contains(e.SourcePath) &&
                e.SourcePath.StartsWith(scanRoot, StringComparison.OrdinalIgnoreCase))
                e.SourceMissing = true;
        }
    }

    /// <summary>(migrated, total) where migrated = Done and total excludes Skipped.</summary>
    public (int Migrated, int Total) Progress()
    {
        int total    = Entries.Count(e => e.Status != BtwMigrationStatus.Skipped);
        int migrated = Entries.Count(e => e.Status == BtwMigrationStatus.Done);
        return (migrated, total);
    }
}

/// <summary>
/// Scans a BarTender template folder and builds .lbl skeletons for the manual rebuild:
/// correct size/name from the .btw header, an optional locked semi-transparent backdrop image to
/// trace over, and optional field seeding from the data file the label used. Full element
/// auto-conversion is deliberately not attempted — the .btw body is a proprietary binary and a
/// wrong-looking auto-import is worse than an honest rebuild (render parity is sacred).
/// </summary>
public static class BtwMigrationService
{
    /// <summary>Name of the field that would make a skeleton backdrop print. Never supplied by
    /// data files, so the backdrop shows on the design canvas but is skipped at print time.</summary>
    public const string BackdropPrintField = "PrintBackdrop";

    /// <summary>Reads every *.btw under <paramref name="folder"/> (recursive) into tracker entries.</summary>
    public static List<BtwMigrationEntry> ScanFolder(string folder)
    {
        var results = new List<BtwMigrationEntry>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.btw", SearchOption.AllDirectories)
                                      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var read = BtwImportService.TryReadHeader(path);
            if (read.Status == BtwImportService.BtwReadStatus.Ok && read.Metadata != null)
            {
                results.Add(new BtwMigrationEntry
                {
                    SourcePath = path,
                    Title      = read.Metadata.Title,
                    WidthMm    = read.Metadata.WidthMm,
                    HeightMm   = read.Metadata.HeightMm,
                    Printer    = read.Metadata.Printer,
                });
            }
            else
            {
                results.Add(new BtwMigrationEntry
                {
                    SourcePath = path,
                    Title      = Path.GetFileNameWithoutExtension(path),
                    Status     = BtwMigrationStatus.Unreadable,
                    Notes      = read.ErrorMessage ?? "Header could not be read.",
                });
            }
        }
        return results;
    }

    /// <summary>
    /// Builds the .lbl skeleton for one tracker entry. <paramref name="backdropImagePath"/> adds a
    /// locked, 35 %-opacity, full-canvas image (scan/screenshot of the old label) on its own layer
    /// as a tracing aid; its print condition references <see cref="BackdropPrintField"/> so it
    /// never prints. <paramref name="dataColumns"/> (headers from the label's CSV/Excel file)
    /// seeds Fields + the column mapping, and <paramref name="dataFilePath"/> becomes the
    /// template's default data connection.
    /// </summary>
    public static LabelTemplate BuildSkeleton(BtwMigrationEntry entry,
        string? backdropImagePath = null,
        IReadOnlyList<(string Letter, string Header)>? dataColumns = null,
        string? dataFilePath = null,
        string? templateName = null)
    {
        var t = new LabelTemplate
        {
            Name     = templateName
                       ?? (string.IsNullOrWhiteSpace(entry.Title)
                           ? Path.GetFileNameWithoutExtension(entry.SourcePath)
                           : entry.Title),
            WidthMm  = entry.WidthMm  > 0 ? entry.WidthMm  : 50.8,
            HeightMm = entry.HeightMm > 0 ? entry.HeightMm : 25.4,
        };

        if (dataColumns is { Count: > 0 })
        {
            foreach (var (letter, header) in dataColumns)
            {
                if (string.IsNullOrWhiteSpace(header)) continue;
                if (t.Fields.Contains(header, StringComparer.OrdinalIgnoreCase)) continue;
                t.Fields.Add(header);
                t.ExcelColumnMapping[header] = letter;
            }
            if (!string.IsNullOrWhiteSpace(dataFilePath)) t.DefaultExcelPath = dataFilePath;
        }

        if (!string.IsNullOrWhiteSpace(backdropImagePath))
        {
            var layer = new Layer { Name = "Backdrop — delete before production" };
            t.Layers.Add(layer);
            t.Elements.Add(new ImageElement
            {
                X = 0, Y = 0, Width = t.WidthPx, Height = t.HeightPx,
                ImagePath = backdropImagePath,
                Opacity = 0.35,
                MaintainAspectRatio = false,
                IsLocked = true,
                LayerId = layer.Id,
                ZIndex = -100,
                Name = "BTW backdrop (tracing aid)",
                // "{Field}" = print only when the field is non-empty; nothing ever supplies it.
                PrintCondition = "{" + BackdropPrintField + "}",
            });
        }

        return t;
    }

    /// <summary>Returns <paramref name="desired"/>, or "desired (2)", "desired (3)", … until
    /// <paramref name="nameTaken"/> says it's free — skeletons must never overwrite an existing template.</summary>
    public static string UniqueTemplateName(string desired, Func<string, bool> nameTaken)
    {
        if (!nameTaken(desired)) return desired;
        for (int i = 2; ; i++)
        {
            var candidate = $"{desired} ({i})";
            if (!nameTaken(candidate)) return candidate;
        }
    }
}
