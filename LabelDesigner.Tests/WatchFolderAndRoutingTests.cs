using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using System.IO;
using Xunit;

namespace LabelDesigner.Tests;

public class TemplateRouterTests
{
    private static Dictionary<string, string> Row(params (string k, string v)[] kvs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in kvs) d[k] = v;
        return d;
    }

    private static TemplateRoute Range(string field, double min, double max, string template) =>
        new() { Field = field, Operator = RouteOperator.NumericRange, Min = min, Max = max, TemplateName = template };

    [Fact]
    public void ExplicitTemplateColumn_BeatsRules()
    {
        var routes = new[] { Range("Volume", 0, 100, "Rule Template") };
        var name = TemplateRouter.ResolveName(Row(("Template", "My Template"), ("Volume", "50")), routes, "Default");
        Assert.Equal("My Template", name);
    }

    [Fact]
    public void FirstMatchingRule_Wins()
    {
        var routes = new[] { Range("Volume", 0, 60, "Small"), Range("Volume", 0, 200, "Big") };
        Assert.Equal("Small", TemplateRouter.ResolveName(Row(("Volume", "50")), routes, null));
        Assert.Equal("Big",   TemplateRouter.ResolveName(Row(("Volume", "120")), routes, null));
    }

    [Fact]
    public void NumericRange_ParsesUnitSuffix()
    {
        var routes = new[] { Range("Volume", 0, 60, "Small") };
        Assert.Equal("Small", TemplateRouter.ResolveName(Row(("Volume", "50ml")), routes, null));
    }

    [Fact]
    public void EqualsAndContains()
    {
        var eq = new TemplateRoute { Field = "Size", Operator = RouteOperator.Equals, Value = "Large", TemplateName = "L" };
        var ct = new TemplateRoute { Field = "Sku", Operator = RouteOperator.Contains, Value = "DT-", TemplateName = "DoorTreats" };
        Assert.True(TemplateRouter.Matches(eq, Row(("Size", " large "))));   // trimmed, case-insensitive
        Assert.False(TemplateRouter.Matches(eq, Row(("Size", "XL"))));
        Assert.True(TemplateRouter.Matches(ct, Row(("Sku", "X-DT-50"))));
        Assert.False(TemplateRouter.Matches(ct, Row(("Sku", "X-50"))));
    }

    [Fact]
    public void NoMatch_FallsBackToDefault_ThenNull()
    {
        var routes = new[] { Range("Volume", 0, 60, "Small") };
        Assert.Equal("Folder Default", TemplateRouter.ResolveName(Row(("Volume", "999")), routes, "Folder Default"));
        Assert.Null(TemplateRouter.ResolveName(Row(("Volume", "999")), routes, null));
        Assert.Null(TemplateRouter.ResolveName(Row(("Volume", "999")), routes, "  "));
    }
}

public class GenericCsvTests
{
    [Fact]
    public void ParsesByHeaderName_QuotesAndBlanks()
    {
        var csv = "Name,Qty,\"Note\"\n\"Apple, Red\",3,\"He said \"\"hi\"\"\"\n,,\nBanana,,last\n";
        var rows = CsvImportService.ParseGeneric(csv);
        Assert.Equal(2, rows.Count);                       // the fully-blank row is dropped
        Assert.Equal("Apple, Red", rows[0]["Name"]);
        Assert.Equal("He said \"hi\"", rows[0]["Note"]);
        Assert.Equal("Banana", rows[1]["name"]);           // case-insensitive keys
        Assert.Equal("", rows[1]["Qty"]);
    }

    [Fact]
    public void MissingTrailingCells_AreEmpty()
    {
        var rows = CsvImportService.ParseGeneric("A,B,C\n1,2\n");
        Assert.Single(rows);
        Assert.Equal("", rows[0]["C"]);
    }
}

public class PrintJobParserTests
{
    private static readonly LabelTemplate Small = new() { Name = "Small" };
    private static readonly LabelTemplate Big = new() { Name = "Big" };

    private static LabelTemplate? Lookup(string name) =>
        name.Equals("Small", StringComparison.OrdinalIgnoreCase) ? Small :
        name.Equals("Big", StringComparison.OrdinalIgnoreCase) ? Big : null;

    private static Dictionary<string, string> Row(params (string k, string v)[] kvs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in kvs) d[k] = v;
        return d;
    }

    [Fact]
    public void GroupsRowsByResolvedTemplate()
    {
        var rows = new List<Dictionary<string, string>>
        {
            Row(("Template", "Small"), ("Name", "a")),
            Row(("Template", "Big"),   ("Name", "b")),
            Row(("Template", "small"), ("Name", "c")),   // case-insensitive grouping
        };
        var job = PrintJobParser.Parse(rows, Lookup, Array.Empty<TemplateRoute>(), null);
        Assert.Equal(2, job.Groups.Count);
        Assert.Equal(2, job.Groups.Single(g => g.Template == Small).Rows.Count);
        Assert.Empty(job.UnroutedRows);
        Assert.Equal(3, job.TotalLabels);   // default qty 1 each
    }

    [Fact]
    public void UnknownAndMissingTemplates_AreReportedNotGuessed()
    {
        var rows = new List<Dictionary<string, string>>
        {
            Row(("Template", "Nope"), ("Name", "a")),
            Row(("Name", "b")),                          // nothing to route by
        };
        var job = PrintJobParser.Parse(rows, Lookup, Array.Empty<TemplateRoute>(), null);
        Assert.Empty(job.Groups);
        Assert.Equal(2, job.UnroutedRows.Count);
        Assert.Contains("Nope", job.UnroutedRows[0].Error);
        Assert.Equal(1, job.UnroutedRows[0].RowNumber);
        Assert.Equal(2, job.UnroutedRows[1].RowNumber);
    }

    [Fact]
    public void RoutesAndDefault_ApplyWhenNoColumn()
    {
        var routes = new[] { new TemplateRoute
            { Field = "Volume", Operator = RouteOperator.NumericRange, Min = 0, Max = 60, TemplateName = "Small" } };
        var rows = new List<Dictionary<string, string>>
        {
            Row(("Volume", "50"), ("Name", "a")),    // rule → Small
            Row(("Volume", "500"), ("Name", "b")),   // no rule → folder default Big
        };
        var job = PrintJobParser.Parse(rows, Lookup, routes, "Big");
        Assert.Single(job.Groups.Single(g => g.Template == Small).Rows);
        Assert.Single(job.Groups.Single(g => g.Template == Big).Rows);
    }

    [Fact]
    public void QtyColumn_Rules()
    {
        Assert.Equal(1, PrintJobParser.ReadQty(Row(("Name", "x"))));                 // no column → 1
        Assert.Equal(1, PrintJobParser.ReadQty(Row(("PrintQty", ""))));              // blank → 1
        Assert.Equal(0, PrintJobParser.ReadQty(Row(("PrintQty", "0"))));             // explicit 0 → skip
        Assert.Equal(7, PrintJobParser.ReadQty(Row(("Qty", "7"))));
        Assert.Equal(4, PrintJobParser.ReadQty(Row(("Copies", "4"))));
        Assert.Equal(0, PrintJobParser.ReadQty(Row(("PrintQty", "lots"))));          // junk → skip, never guess
        Assert.Equal(0, PrintJobParser.ReadQty(Row(("PrintQty", "-2"))));            // negative → skip
    }
}

public class WatchFolderFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ld-wf-" + Guid.NewGuid().ToString("N"));

    public WatchFolderFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void EnsureDirs_CreatesStandardLayout()
    {
        var dirs = WatchFolderService.EnsureDirs(_root);
        Assert.True(Directory.Exists(dirs.Inbox));
        Assert.True(Directory.Exists(dirs.Processing));
        Assert.True(Directory.Exists(dirs.Printed));
        Assert.True(Directory.Exists(dirs.Failed));
        Assert.Contains(Environment.MachineName, dirs.Processing);   // per-station processing
    }

    [Fact]
    public void TryClaim_MovesFile_AndFailsWhileFileIsLocked()
    {
        var dirs = WatchFolderService.EnsureDirs(_root);
        var file = Path.Combine(dirs.Inbox, "job.csv");
        File.WriteAllText(file, "A\n1\n");

        Assert.True(WatchFolderService.TryClaim(file, dirs.Processing, out var claimed));
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(claimed));

        // A file still held open by the writer (ERP mid-write) cannot be claimed.
        var locked = Path.Combine(dirs.Inbox, "busy.csv");
        using (var fs = new FileStream(locked, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.WriteByte((byte)'x');
            fs.Flush();
            Assert.False(WatchFolderService.TryClaim(locked, dirs.Processing, out _));
        }
        Assert.True(File.Exists(locked));   // untouched, retried next scan
    }

    [Fact]
    public void MoveToUnique_SuffixesOnCollision()
    {
        var dest = Path.Combine(_root, "printed");
        var a = Path.Combine(_root, "job.csv");
        File.WriteAllText(a, "1");
        var first = WatchFolderService.MoveToUnique(a, dest);

        var b = Path.Combine(_root, "job.csv");
        File.WriteAllText(b, "2");
        var second = WatchFolderService.MoveToUnique(b, dest);

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal("job (1).csv", Path.GetFileName(second));
    }

    [Fact]
    public void RecoverOrphans_ReturnsCrashLeftoversToInbox()
    {
        var dirs = WatchFolderService.EnsureDirs(_root);
        File.WriteAllText(Path.Combine(dirs.Processing, "stuck.csv"), "x");
        int n = WatchFolderService.RecoverOrphans(dirs.Processing, dirs.Inbox);
        Assert.Equal(1, n);
        Assert.True(File.Exists(Path.Combine(dirs.Inbox, "stuck.csv")));
        Assert.Empty(Directory.GetFiles(dirs.Processing));
    }
}
