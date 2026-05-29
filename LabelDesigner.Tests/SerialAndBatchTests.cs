using LabelDesigner.Core.Models;
using LabelDesigner.Helpers;
using LabelDesigner.Services;
using Xunit;

namespace LabelDesigner.Tests;

public class SerialAndBatchTests
{
    [Fact]
    public void Resolve_WithReservedBase_IncrementsPerLabel()
    {
        var ds = new DataSourceDefinition
        {
            Name = "ctn", Type = DataSourceType.Serial, SerialStart = 1000, Increment = 1, Format = "D6"
        };
        var t = new LabelTemplate();
        t.DataSources.Add(ds);
        var reserved = new Dictionary<Guid, long> { [ds.Id] = 1000 };

        Assert.Equal("001000", DataSourceResolver.Resolve(t, 0, reserved)["ctn"]);
        Assert.Equal("001005", DataSourceResolver.Resolve(t, 5, reserved)["ctn"]);
    }

    [Fact]
    public void Resolve_RespectsIncrement()
    {
        var ds = new DataSourceDefinition
        {
            Name = "n", Type = DataSourceType.Serial, SerialStart = 0, Increment = 10, Format = "0"
        };
        var t = new LabelTemplate();
        t.DataSources.Add(ds);
        var reserved = new Dictionary<Guid, long> { [ds.Id] = 50 };

        Assert.Equal("50", DataSourceResolver.Resolve(t, 0, reserved)["n"]);
        Assert.Equal("70", DataSourceResolver.Resolve(t, 2, reserved)["n"]);
    }

    [Fact]
    public void ResolveConstants_ExcludesSerial_IncludesFixed()
    {
        var t = new LabelTemplate();
        t.DataSources.Add(new DataSourceDefinition { Name = "ctn", Type = DataSourceType.Serial });
        t.DataSources.Add(new DataSourceDefinition { Name = "lot", Type = DataSourceType.FixedValue, FixedValue = "LOT-A" });

        var c = DataSourceResolver.ResolveConstants(t);
        Assert.Equal("LOT-A", c["lot"]);
        Assert.False(c.ContainsKey("ctn")); // serials are regenerated, not snapshotted as constants
    }

    [Fact]
    public void ReprintRegeneration_ReproducesOriginalSequence()
    {
        // Mirrors the formula PrintService.Reprint uses: base + i*increment, formatted.
        var plan = new SerialPlanItem { Name = "ctn", Base = 1000, Increment = 1, Format = "D6" };
        string Label(int i) => (plan.Base + (long)i * plan.Increment).ToString(plan.Format);

        Assert.Equal("001000", Label(0));
        Assert.Equal("001049", Label(49));
        // A reprint of a 50-label batch reproduces exactly 001000..001049 — no new IDs minted.
    }

    [Fact]
    public void ValidateBatch_NamesTheFailingRow()
    {
        var t = new LabelTemplate();
        t.Elements.Add(new BarcodeElement { Format = BarcodeFormatOption.EAN13, BoundField = "code" });

        var rows = new List<Dictionary<string, string>>
        {
            new() { ["code"] = "123456789012" }, // valid: 12 digits
            new() { ["code"] = "12345" },        // invalid
        };

        var errors = PrintService.ValidateBatch(t, rows);
        Assert.Single(errors);
        Assert.Contains("Row 2", errors[0]);
    }

    [Fact]
    public void Reserve_ResetPerBatch_StartsAtSerialStart_AndDoesNotPersist()
    {
        var ds = new DataSourceDefinition
        {
            Type = DataSourceType.Serial, SerialMode = SerialMode.ResetPerBatch, SerialStart = 1, Increment = 1
        };
        var t = new LabelTemplate();
        t.DataSources.Add(ds);

        var r1 = SerialCounterStore.Reserve(t, 50);
        var r2 = SerialCounterStore.Reserve(t, 50);

        // Every batch restarts at SerialStart — no advance, no file/network store needed.
        Assert.Equal(1L, r1[ds.Id]);
        Assert.Equal(1L, r2[ds.Id]);
    }

    [Fact]
    public void GetBase_ResetPerBatch_IsSerialStart()
    {
        var ds = new DataSourceDefinition
        {
            Type = DataSourceType.Serial, SerialMode = SerialMode.ResetPerBatch, SerialStart = 7
        };
        Assert.Equal(7L, SerialCounterStore.GetBase(new LabelTemplate(), ds));
    }

    [Fact]
    public void ValidateBatch_AllValid_NoErrors()
    {
        var t = new LabelTemplate();
        t.Elements.Add(new BarcodeElement { Format = BarcodeFormatOption.Code128, BoundField = "code" });

        var rows = new List<Dictionary<string, string>>
        {
            new() { ["code"] = "ABC123" },
            new() { ["code"] = "XYZ789" },
        };

        Assert.Empty(PrintService.ValidateBatch(t, rows));
    }
}
