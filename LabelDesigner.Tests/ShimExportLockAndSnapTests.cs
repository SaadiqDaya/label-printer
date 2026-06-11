using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Designer;
using LabelDesigner.Services;
using LabelDesigner.ViewModels;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LabelDesigner.Tests;

// ─── HTTP shim API: request parsing + pure helpers ──────────────────────────────

public class ShimContractTests
{
    private static readonly JsonSerializerOptions WireJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Request_ParsesCamelCaseShimBody()
    {
        const string body = """
        {
          "templatePath": "C:\\Apps\\Dashboard\\bartender-templates\\DoorTreats-50ml.btw",
          "printerName": "Zebra ZD621",
          "jobName": "Order 1234",
          "labels": [ { "ProductName": "Mint", "LotNumber": "L1" }, { "productname": "Mint", "lotnumber": "L1" } ]
        }
        """;
        var req = JsonSerializer.Deserialize<ShimPrintRequest>(body, WireJson)!;
        Assert.Equal("Zebra ZD621", req.PrinterName);
        Assert.Equal("Order 1234", req.JobName);
        Assert.Equal(2, req.Labels.Count);
        Assert.Equal("Mint", req.Labels[0]["ProductName"]);
    }

    [Theory]
    [InlineData(@"C:\Apps\Dashboard\bartender-templates\DoorTreats-50ml.btw", "DoorTreats-50ml")]
    [InlineData("DoorTreats-50ml.btw", "DoorTreats-50ml")]
    [InlineData("subdir/My Label.lbl", "My Label")]
    [InlineData("My Label", "My Label")]
    public void TemplateStem_StripsPathAndExtension(string input, string expected) =>
        Assert.Equal(expected, HttpPrintService.TemplateStem(input));

    [Fact]
    public void GroupConsecutive_CollapsesRunsOfIdenticalLabels()
    {
        Dictionary<string, string> L(string v) => new() { ["A"] = v };
        var groups = HttpPrintService.GroupConsecutive(new[] { L("x"), L("x"), L("x"), L("y"), L("x") });
        Assert.Equal(3, groups.Count);
        Assert.Equal(3, groups[0].Copies);
        Assert.Equal("x", groups[0].Fields["A"]);
        Assert.Equal(1, groups[1].Copies);
        Assert.Equal("y", groups[1].Fields["A"]);
        Assert.Equal(1, groups[2].Copies);   // non-adjacent duplicates stay separate (order preserved)
    }

    [Fact]
    public void GroupConsecutive_ValuesAreCaseSensitive_KeysAreNot()
    {
        var a = new Dictionary<string, string> { ["Lot"] = "L1" };
        var b = new Dictionary<string, string> { ["lot"] = "L1" };   // same pair, different key casing
        var c = new Dictionary<string, string> { ["Lot"] = "l1" };   // different VALUE casing = different label
        Assert.Single(HttpPrintService.GroupConsecutive(new[] { a, b }));
        Assert.Equal(2, HttpPrintService.GroupConsecutive(new[] { a, c }).Count);
    }
}

// ─── PDF export ─────────────────────────────────────────────────────────────────

public class PdfExporterTests
{
    private static System.Windows.Media.Imaging.BitmapSource TestImage(int w, int h)
    {
        var pixels = new byte[w * h * 3];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = 200;
        return System.Windows.Media.Imaging.BitmapSource.Create(
            w, h, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null, pixels, w * 3);
    }

    [Fact]
    public void Build_ProducesParsablePdfSkeleton()
    {
        // 50.8 × 25.4 mm is the DoorTreats label: exactly 144 × 72 pt.
        var bytes = PdfExporter.Build(TestImage(406, 203), widthMm: 50.8, heightMm: 25.4);
        var text = Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/MediaBox [0 0 144 72]", text);
        Assert.Contains("/Subtype /Image", text);
        Assert.Contains("/Width 406 /Height 203", text);
        Assert.Contains("/Filter /FlateDecode", text);

        // The xref offsets must point at the actual object headers.
        foreach (var objNum in new[] { 1, 2, 3, 4, 5 })
        {
            var marker = $"\n{objNum} 0 obj";
            Assert.Contains(marker, text);
        }
        var startxref = text[(text.IndexOf("startxref") + "startxref".Length)..].Trim().Split('\n')[0];
        Assert.Equal("xref", text.Substring(int.Parse(startxref), 4));
    }
}

// ─── Smart-snap math ────────────────────────────────────────────────────────────

public class SnapSolverTests
{
    [Fact]
    public void SnapsToNearestTargetWithinThreshold()
    {
        var (corr, guide) = SnapSolver.Solve(new[] { 10.0, 50.0 }, new[] { 12.0, 100.0 }, threshold: 3);
        Assert.Equal(2.0, corr);     // 10 → 12
        Assert.Equal(12.0, guide);
    }

    [Fact]
    public void NoSnapOutsideThreshold()
    {
        var (corr, guide) = SnapSolver.Solve(new[] { 10.0 }, new[] { 20.0 }, threshold: 3);
        Assert.Equal(0.0, corr);
        Assert.Null(guide);
    }

    [Fact]
    public void PicksTheClosestPairWhenSeveralMatch()
    {
        var (corr, guide) = SnapSolver.Solve(new[] { 10.0, 30.0 }, new[] { 12.5, 30.5 }, threshold: 3);
        Assert.Equal(0.5, corr);     // 30 → 30.5 beats 10 → 12.5
        Assert.Equal(30.5, guide);
    }
}

// ─── Name / lock / group: persistence + designer behaviour ─────────────────────

public class LockAndGroupTests
{
    [Fact]
    public void NameLockAndGroup_SurviveTemplateRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ld-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var gid = Guid.NewGuid();
            var template = new LabelTemplate { Name = "RoundTrip" };
            template.Elements.Add(new TextElement { Name = "Lot text", IsLocked = true, GroupId = gid });
            template.Elements.Add(new BarcodeElement { GroupId = gid });

            var svc = new TemplateService(dir);
            var path = Path.Combine(dir, "rt.lbl");
            svc.Save(template, path);
            var loaded = svc.Load(path)!;

            var text = Assert.IsType<TextElement>(loaded.Elements[0]);
            Assert.Equal("Lot text", text.Name);
            Assert.True(text.IsLocked);
            Assert.Equal(gid, text.GroupId);
            Assert.Equal(gid, loaded.Elements[1].GroupId);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void OldTemplateJson_LoadsWithDefaults()
    {
        // A v2 element payload that predates Name/IsLocked/GroupId.
        const string json = """
        { "Name": "Legacy", "Elements": [ { "$type": "Text", "Text": "hi", "X": 1, "Y": 2 } ] }
        """;
        var t = JsonSerializer.Deserialize<LabelTemplate>(json, TemplateService.JsonOptions)!;
        var el = t.Elements[0];
        Assert.Equal("", el.Name);
        Assert.False(el.IsLocked);
        Assert.Null(el.GroupId);
    }

    [Fact]
    public void GroupCommand_AssignsOneSharedId_AndUngroupClearsWholeGroup()
    {
        var vm = new DesignerViewModel();
        var a = new TextElementViewModel();
        var b = new TextElementViewModel();
        var c = new TextElementViewModel();
        vm.AddElement(a, recordUndo: false);
        vm.AddElement(b, recordUndo: false);
        vm.AddElement(c, recordUndo: false);

        vm.SelectedElements.Add(a);
        vm.SelectedElements.Add(b);
        vm.GroupSelected();

        Assert.NotNull(a.GroupId);
        Assert.Equal(a.GroupId, b.GroupId);
        Assert.Null(c.GroupId);

        // Ungrouping with only ONE member selected dissolves the whole group.
        vm.SelectedElements.Clear();
        vm.SelectedElements.Add(a);
        vm.UngroupSelected();
        Assert.Null(a.GroupId);
        Assert.Null(b.GroupId);
    }

    [Fact]
    public void GroupAndUngroup_AreUndoable()
    {
        var vm = new DesignerViewModel();
        var a = new TextElementViewModel();
        var b = new TextElementViewModel();
        vm.AddElement(a, recordUndo: false);
        vm.AddElement(b, recordUndo: false);
        vm.SelectedElements.Add(a);
        vm.SelectedElements.Add(b);

        vm.GroupSelected();
        var gid = a.GroupId;
        Assert.NotNull(gid);

        vm.UndoManager.Undo();
        Assert.Null(a.GroupId);
        Assert.Null(b.GroupId);

        vm.UndoManager.Redo();
        Assert.Equal(gid, a.GroupId);
        Assert.Equal(gid, b.GroupId);
    }

    [Fact]
    public void DeleteSelected_SkipsLockedElements()
    {
        var vm = new DesignerViewModel();
        var locked = new TextElementViewModel { IsLocked = true };
        var free = new TextElementViewModel();
        vm.AddElement(locked, recordUndo: false);
        vm.AddElement(free, recordUndo: false);

        vm.SelectedElements.Add(locked);
        vm.SelectedElements.Add(free);
        vm.DeleteSelected();

        Assert.Contains(locked, vm.Elements);
        Assert.DoesNotContain(free, vm.Elements);
    }

    [Fact]
    public void DuplicateViaCloneOrModel_DoesNotInheritGroup()
    {
        var gid = Guid.NewGuid();
        var el = new TextElement { Name = "N", IsLocked = true, GroupId = gid };
        var clone = (TextElement)el.Clone();
        Assert.Equal("N", clone.Name);
        Assert.True(clone.IsLocked);
        Assert.Null(clone.GroupId);
    }

    [Fact]
    public void UserGivenName_WinsInDisplayName()
    {
        var vm = new TextElementViewModel { Text = "Hello" };
        Assert.Equal("Text \"Hello\"", vm.DisplayName);
        vm.Name = "Lot label";
        Assert.Equal("Lot label", vm.DisplayName);
        vm.Name = "";
        Assert.Equal("Text \"Hello\"", vm.DisplayName);
    }
}
