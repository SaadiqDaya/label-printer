using System.IO;
using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using Xunit;

namespace LabelDesigner.Tests;

public class DataAndFieldTests
{
    // ─── CSV import (RFC-4180) ──────────────────────────────────────────────
    [Fact]
    public void Csv_ParsesQuotedCommas_AndPrintQty()
    {
        var t = new LabelTemplate();
        t.ExcelColumnMapping["name"] = "A";
        t.ExcelColumnMapping["city"] = "B";
        t.PrintQtyColumn = "C";

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllText(path, "name,city,qty\nAlice,\"New York, NY\",2\nBob,LA,0\n");
        try
        {
            var rows = CsvImportService.Load(path, t);
            Assert.Equal(2, rows.Count);
            Assert.Equal("Alice", rows[0].Fields["name"]);
            Assert.Equal("New York, NY", rows[0].Fields["city"]); // quoted comma preserved
            Assert.Equal(2, rows[0].PrintQty);
            Assert.Equal(0, rows[1].PrintQty);                    // 0 = skip
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Csv_DoubledQuotes_Unescaped()
    {
        var t = new LabelTemplate();
        t.ExcelColumnMapping["note"] = "A";
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllText(path, "note\n\"She said \"\"hi\"\"\"\n");
        try
        {
            var rows = CsvImportService.Load(path, t);
            Assert.Single(rows);
            Assert.Equal("She said \"hi\"", rows[0].Fields["note"]);
        }
        finally { File.Delete(path); }
    }

    // ─── FieldDefinition default fill + required (via the public Validate path) ──
    [Fact]
    public void Validate_RequiredFieldWithDefault_DoesNotError()
    {
        var t = new LabelTemplate();
        t.FieldDefinitions.Add(new FieldDefinition { Name = "lot", Required = true, DefaultValue = "L001" });
        var errors = PrintService.Validate(t, new Dictionary<string, string>());
        Assert.Empty(errors); // default fills the blank before the required check
    }

    [Fact]
    public void Validate_RequiredFieldNoDefault_Errors()
    {
        var t = new LabelTemplate();
        t.FieldDefinitions.Add(new FieldDefinition { Name = "lot", Required = true });
        var errors = PrintService.Validate(t, new Dictionary<string, string>());
        Assert.Single(errors);
        Assert.Contains("required", errors[0]);
    }

    // ─── FieldValidator rules ───────────────────────────────────────────────
    [Fact]
    public void FieldValidator_NumberRange()
    {
        var fd = new FieldDefinition { Name = "price", DataType = FieldDataType.Number, Min = 0, Max = 100 };
        Assert.Null(FieldValidator.Validate(fd, "50"));
        Assert.NotNull(FieldValidator.Validate(fd, "150"));
        Assert.NotNull(FieldValidator.Validate(fd, "abc"));
    }

    [Fact]
    public void FieldValidator_AllowedValues()
    {
        var fd = new FieldDefinition { Name = "grade" };
        fd.AllowedValues.AddRange(new[] { "A", "B", "C" });
        Assert.Null(FieldValidator.Validate(fd, "b"));        // case-insensitive
        Assert.NotNull(FieldValidator.Validate(fd, "Z"));
    }

    [Fact]
    public void FieldValidator_LengthAndPattern()
    {
        var fd = new FieldDefinition { Name = "sku", MinLength = 3, MaxLength = 5, Pattern = "[A-Z0-9]+" };
        Assert.Null(FieldValidator.Validate(fd, "AB12"));
        Assert.NotNull(FieldValidator.Validate(fd, "AB"));     // too short
        Assert.NotNull(FieldValidator.Validate(fd, "ABCDEF")); // too long
        Assert.NotNull(FieldValidator.Validate(fd, "ab-1"));   // pattern fails
    }

    [Fact]
    public void FieldValidator_OptionalEmpty_IsOk()
    {
        var fd = new FieldDefinition { Name = "x", MinLength = 3 };
        Assert.Null(FieldValidator.Validate(fd, ""));          // optional + empty → fine
    }
}
