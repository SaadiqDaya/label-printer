using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using Xunit;

namespace LabelDesigner.Tests;

public class BarcodeValidatorTests
{
    [Theory]
    [InlineData("123456789012", null)]          // 12 digits → ok (check digit added)
    [InlineData("1234567890128", null)]         // 13 digits with valid check
    [InlineData("12345", "needs")]              // too short
    [InlineData("12345678901A", "digits")]      // non-digit
    public void Ean13(string value, string? expectContains)
    {
        var err = BarcodeValidator.Validate(BarcodeFormatOption.EAN13, value);
        if (expectContains == null) Assert.Null(err);
        else Assert.Contains(expectContains, err);
    }

    [Theory]
    [InlineData("1234", null)]      // even
    [InlineData("123", "even")]     // odd
    public void Itf_RequiresEvenDigits(string value, string? expectContains)
    {
        var err = BarcodeValidator.Validate(BarcodeFormatOption.ITF, value);
        if (expectContains == null) Assert.Null(err);
        else Assert.Contains(expectContains, err);
    }

    [Theory]
    [InlineData("ABC-123", null)]
    [InlineData("abc", null)]              // lowercased then valid
    [InlineData("hello@world", "cannot")] // '@' not in Code39 set
    public void Code39_Charset(string value, string? expectContains)
    {
        var err = BarcodeValidator.Validate(BarcodeFormatOption.Code39, value);
        if (expectContains == null) Assert.Null(err);
        else Assert.Contains(expectContains, err);
    }

    [Fact]
    public void Gs1_BalancedParens()
    {
        Assert.Null(BarcodeValidator.Validate(BarcodeFormatOption.GS1_128, "(01)09501101"));
        Assert.NotNull(BarcodeValidator.Validate(BarcodeFormatOption.GS1_128, "(01)09501101)"));
    }

    [Fact]
    public void Empty_IsError()
    {
        Assert.NotNull(BarcodeValidator.Validate(BarcodeFormatOption.Code128, ""));
    }

    [Fact]
    public void Ean13_CheckDigit_IsCorrect()
    {
        // Known GS1 example: 400638133393 → check digit 1 → 4006381333931
        Assert.Equal('1', BarcodeValidator.Ean13CheckDigit("400638133393"));
    }

    [Fact]
    public void Normalize_AppendsEan13CheckDigit_When12Digits()
    {
        var result = BarcodeValidator.NormalizeForEncoding(BarcodeFormatOption.EAN13, "400638133393");
        Assert.Equal("4006381333931", result);
        Assert.Equal(13, result.Length);
    }
}
