using System.IO;
using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using Xunit;

namespace LabelDesigner.Tests;

public class BtwMigrationTests : IDisposable
{
    private readonly string _dir;

    public BtwMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "btwmig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp cleanup best-effort */ }
    }

    private string WriteFakeBtw(string name, string title, string size)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path,
            "Bar Tender Format File\n" +
            $"<Metadata><Title>{title}</Title><TemplateSize>{size}</TemplateSize>" +
            "<Printer>Zebra ZD621</Printer><Application>BarTender</Application></Metadata>");
        return path;
    }

    // ── Scan ───────────────────────────────────────────────────────────────────
    [Fact]
    public void ScanFolder_ReadsMetadata_AndFlagsUnreadable()
    {
        WriteFakeBtw("good.btw", "Bin Label", "66.7 x 25.4 mm");
        File.WriteAllText(Path.Combine(_dir, "junk.btw"), "not a bartender file at all");
        var sub = Directory.CreateDirectory(Path.Combine(_dir, "sub")).FullName;
        File.WriteAllText(Path.Combine(sub, "nested.btw"),
            "Bar Tender Format File\n<Metadata><Title>Nested</Title><TemplateSize>50.8 x 25.4 mm</TemplateSize></Metadata>");

        var scanned = BtwMigrationService.ScanFolder(_dir);

        Assert.Equal(3, scanned.Count);
        var good = scanned.Single(e => e.FileName == "good.btw");
        Assert.Equal(BtwMigrationStatus.Pending, good.Status);
        Assert.Equal("Bin Label", good.Title);
        Assert.Equal(66.7, good.WidthMm);
        Assert.Equal(25.4, good.HeightMm);
        Assert.Equal("Zebra ZD621", good.Printer);

        var junk = scanned.Single(e => e.FileName == "junk.btw");
        Assert.Equal(BtwMigrationStatus.Unreadable, junk.Status);
        Assert.False(string.IsNullOrWhiteSpace(junk.Notes));

        Assert.Contains(scanned, e => e.FileName == "nested.btw");   // recursive
    }

    // ── Merge ──────────────────────────────────────────────────────────────────
    [Fact]
    public void MergeScan_PreservesWork_RefreshesMetadata_FlagsMissing()
    {
        var store = new BtwMigrationStore();
        var keep = new BtwMigrationEntry
        {
            SourcePath = Path.Combine(_dir, "a.btw"), Title = "Old title",
            WidthMm = 10, HeightMm = 10,
            Status = BtwMigrationStatus.Done, TargetTemplateName = "A rebuilt", Notes = "verified"
        };
        var gone = new BtwMigrationEntry
        {
            SourcePath = Path.Combine(_dir, "deleted.btw"), Status = BtwMigrationStatus.Pending
        };
        store.Entries.AddRange([keep, gone]);

        var rescanOfA = new BtwMigrationEntry
        {
            SourcePath = keep.SourcePath, Title = "New title", WidthMm = 66.7, HeightMm = 25.4
        };
        var brandNew = new BtwMigrationEntry { SourcePath = Path.Combine(_dir, "new.btw") };

        store.MergeScan(_dir, [rescanOfA, brandNew]);

        Assert.Equal(3, store.Entries.Count);
        // Existing entry keeps its migration work but refreshes header metadata.
        Assert.Equal(BtwMigrationStatus.Done, keep.Status);
        Assert.Equal("A rebuilt", keep.TargetTemplateName);
        Assert.Equal("verified", keep.Notes);
        Assert.Equal("New title", keep.Title);
        Assert.Equal(66.7, keep.WidthMm);
        // Vanished file is flagged, never dropped.
        Assert.True(gone.SourceMissing);
        Assert.Contains(store.Entries, e => e.SourcePath == brandNew.SourcePath);
    }

    [Fact]
    public void MergeScan_UnreadableEntry_DoesNotClobberMetadata_AndRecoversWhenReadable()
    {
        var store = new BtwMigrationStore();
        var entry = new BtwMigrationEntry
        {
            SourcePath = Path.Combine(_dir, "x.btw"), Title = "Good", WidthMm = 50.8, HeightMm = 25.4,
            Status = BtwMigrationStatus.Unreadable
        };
        store.Entries.Add(entry);

        // Re-scan now parses the file fine → entry becomes workable again, metadata refreshed.
        store.MergeScan(_dir, [new BtwMigrationEntry
        {
            SourcePath = entry.SourcePath, Title = "Good", WidthMm = 50.8, HeightMm = 25.4
        }]);
        Assert.Equal(BtwMigrationStatus.Pending, entry.Status);

        // A later unreadable scan result must not wipe the known-good size.
        store.MergeScan(_dir, [new BtwMigrationEntry
        {
            SourcePath = entry.SourcePath, Status = BtwMigrationStatus.Unreadable
        }]);
        Assert.Equal(50.8, entry.WidthMm);
        Assert.Equal("Good", entry.Title);
    }

    // ── Store persistence ──────────────────────────────────────────────────────
    [Fact]
    public void Store_SaveLoad_RoundTrips()
    {
        var store = new BtwMigrationStore { LastFolder = _dir };
        store.Entries.Add(new BtwMigrationEntry
        {
            SourcePath = Path.Combine(_dir, "a.btw"), Title = "A", WidthMm = 66.7, HeightMm = 25.4,
            Status = BtwMigrationStatus.InProgress, TargetTemplateName = "A", Notes = "half done",
            UpdatedUtc = new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc)
        });
        store.Save(_dir);
        store.Save(_dir);   // second save exercises the File.Replace path

        var back = BtwMigrationStore.Load(_dir);
        Assert.Equal(_dir, back.LastFolder);
        var e = Assert.Single(back.Entries);
        Assert.Equal(BtwMigrationStatus.InProgress, e.Status);
        Assert.Equal("half done", e.Notes);
        Assert.Equal(66.7, e.WidthMm);
    }

    [Fact]
    public void Store_Progress_CountsDoneOverNonSkipped()
    {
        var store = new BtwMigrationStore();
        store.Entries.AddRange(
        [
            new BtwMigrationEntry { Status = BtwMigrationStatus.Done },
            new BtwMigrationEntry { Status = BtwMigrationStatus.Pending },
            new BtwMigrationEntry { Status = BtwMigrationStatus.Skipped },
        ]);
        var (migrated, total) = store.Progress();
        Assert.Equal(1, migrated);
        Assert.Equal(2, total);
    }

    // ── Skeleton ───────────────────────────────────────────────────────────────
    [Fact]
    public void BuildSkeleton_UsesHeaderSize_AndFallsBackWhenUnknown()
    {
        var t = BtwMigrationService.BuildSkeleton(new BtwMigrationEntry
        {
            SourcePath = @"C:\x\Bin.btw", Title = "Bin Label", WidthMm = 66.7, HeightMm = 25.4
        });
        Assert.Equal("Bin Label", t.Name);
        Assert.Equal(66.7, t.WidthMm);
        Assert.Equal(25.4, t.HeightMm);
        Assert.Empty(t.Elements);

        var fb = BtwMigrationService.BuildSkeleton(new BtwMigrationEntry { SourcePath = @"C:\x\Mystery.btw" });
        Assert.Equal("Mystery", fb.Name);          // filename fallback
        Assert.Equal(50.8, fb.WidthMm);            // 2×1 fallback size
        Assert.Equal(25.4, fb.HeightMm);
    }

    [Fact]
    public void BuildSkeleton_Backdrop_IsLockedTranslucentAndNeverPrints()
    {
        var t = BtwMigrationService.BuildSkeleton(
            new BtwMigrationEntry { SourcePath = @"C:\x\a.btw", Title = "A", WidthMm = 50.8, HeightMm = 25.4 },
            backdropImagePath: @"C:\scans\a.png");

        var layer = Assert.Single(t.Layers);
        Assert.Contains("delete before production", layer.Name, StringComparison.OrdinalIgnoreCase);

        var img = Assert.IsType<ImageElement>(Assert.Single(t.Elements));
        Assert.Equal(@"C:\scans\a.png", img.ImagePath);
        Assert.True(img.IsLocked);
        Assert.Equal(0.35, img.Opacity);
        Assert.Equal(layer.Id, img.LayerId);
        Assert.Equal(t.WidthPx, img.Width, 3);
        Assert.Equal(t.HeightPx, img.Height, 3);
        // The print condition references a field nothing supplies → skipped at print time.
        Assert.Equal("{" + BtwMigrationService.BackdropPrintField + "}", img.PrintCondition);
        Assert.False(Services.ConditionEvaluator.Evaluate(img.PrintCondition,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void BuildSkeleton_SeedsFieldsAndMappingFromDataColumns()
    {
        var t = BtwMigrationService.BuildSkeleton(
            new BtwMigrationEntry { SourcePath = @"C:\x\a.btw", Title = "A", WidthMm = 50.8, HeightMm = 25.4 },
            dataColumns:
            [
                ("A", "PartNumber"), ("B", "PartDescription"), ("C", "Qty"),
                ("D", ""),                     // blank header skipped
                ("E", "partnumber"),           // case-insensitive duplicate skipped
            ],
            dataFilePath: @"C:\data\CycleCount.csv");

        Assert.Equal(new[] { "PartNumber", "PartDescription", "Qty" }, t.Fields);
        Assert.Equal("A", t.ExcelColumnMapping["PartNumber"]);
        Assert.Equal("C", t.ExcelColumnMapping["Qty"]);
        Assert.Equal(@"C:\data\CycleCount.csv", t.DefaultExcelPath);
    }

    [Fact]
    public void UniqueTemplateName_AppendsCounterUntilFree()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Bin", "Bin (2)" };
        Assert.Equal("Fresh", BtwMigrationService.UniqueTemplateName("Fresh", taken.Contains));
        Assert.Equal("Bin (3)", BtwMigrationService.UniqueTemplateName("Bin", taken.Contains));
    }
}
