using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using Xunit;

namespace LabelDesigner.Tests;

public class SerialFormattingTests
{
    [Fact]
    public void PrefixSuffix_Decimal()
        => Assert.Equal("CTN-0005-A", SerialFormatting.Format(5, "CTN-", "-A", 10, 0, "D4"));

    [Fact]
    public void Base36_Padded()
    {
        Assert.Equal("0010", SerialFormatting.Format(36, "", "", 36, 4, ""));   // 36 → "10"
        Assert.Equal("000Z", SerialFormatting.Format(35, "", "", 36, 4, ""));   // 35 → "Z"
        Assert.Equal("Z", SerialFormatting.ToBase36(35));
        Assert.Equal("10", SerialFormatting.ToBase36(36));
    }

    [Fact]
    public void Decimal_UsesFormat()
        => Assert.Equal("007", SerialFormatting.Format(7, "", "", 10, 0, "D3"));
}

public class FormulaEvaluatorTests
{
    private static Dictionary<string, string> F => new(StringComparer.OrdinalIgnoreCase)
    { ["sku"] = "abc", ["qty"] = "3", ["lot"] = "L1" };

    [Fact]
    public void Concat_And_Upper()
        => Assert.Equal("ABC-L1", FormulaEvaluator.Evaluate("UPPER({sku}) & \"-\" & {lot}", F));

    [Fact]
    public void Arithmetic()
    {
        Assert.Equal("6", FormulaEvaluator.Evaluate("{qty} * 2", F));
        Assert.Equal("4", FormulaEvaluator.Evaluate("{qty} + 1", F));
        Assert.Equal("3", FormulaEvaluator.Evaluate("ROUND(2.6, 0)", F));
    }

    [Fact]
    public void StringFunctions()
    {
        Assert.Equal("ab", FormulaEvaluator.Evaluate("LEFT({sku},2)", F));
        Assert.Equal("bc", FormulaEvaluator.Evaluate("RIGHT({sku},2)", F));
        Assert.Equal("3", FormulaEvaluator.Evaluate("LEN({sku})", F));
    }

    [Fact]
    public void Malformed_ReturnsEmpty()
        => Assert.Equal("", FormulaEvaluator.Evaluate("UPPER(", F));   // never throws on a print run
}

public class ZplRendererTests
{
    [Fact]
    public void Render_EmitsLabelFrame_NativeTextAndBarcode()
    {
        var t = new LabelTemplate { Name = "T", Dpi = 203 };
        t.Elements.Add(new TextElement { Text = "HELLO", X = 10, Y = 10, Width = 100, Height = 20 });
        t.Elements.Add(new BarcodeElement { BarcodeValue = "12345", Format = BarcodeFormatOption.Code128, X = 10, Y = 40, Width = 120, Height = 50 });

        var zpl = ZplRenderer.Render(t, new Dictionary<string, string>());

        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl);
        Assert.Contains("^FDHELLO^FS", zpl);   // native text
        Assert.Contains("^BC", zpl);           // native Code128
        Assert.Contains("12345", zpl);
        Assert.Contains("^PW", zpl);           // print width set
    }

    [Fact]
    public void Render_HonorsPrintCondition()
    {
        var t = new LabelTemplate { Dpi = 203 };
        t.Elements.Add(new TextElement { Text = "SHOWN", PrintCondition = "{flag} == \"yes\"", Width = 80, Height = 20 });
        var hidden = ZplRenderer.Render(t, new Dictionary<string, string> { ["flag"] = "no" });
        var shown  = ZplRenderer.Render(t, new Dictionary<string, string> { ["flag"] = "yes" });
        Assert.DoesNotContain("SHOWN", hidden);
        Assert.Contains("SHOWN", shown);
    }
}
