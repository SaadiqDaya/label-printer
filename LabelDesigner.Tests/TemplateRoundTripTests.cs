using System.Text.Json;
using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using Xunit;

namespace LabelDesigner.Tests;

public class TemplateRoundTripTests
{
    [Fact]
    public void NewProps_SurviveRoundTrip()
    {
        var t = new LabelTemplate { Name = "T", Dpi = 300 };
        t.PrinterProfile.Darkness = 18;
        t.PrinterProfile.SpeedIps = 4;
        t.Fields.Add("sku");
        t.FieldDefinitions.Add(new FieldDefinition { Name = "sku", Required = true, Prompt = "SKU" });
        t.Elements.Add(new BarcodeElement
        {
            BarcodeValue = "123", Format = BarcodeFormatOption.GS1_128,
            Rotation = 90, XDimensionMm = 0.25, QuietZoneMm = 3, ErrorCorrectionLevel = 2
        });

        var json = JsonSerializer.Serialize(t, TemplateService.JsonOptions);
        var back = JsonSerializer.Deserialize<LabelTemplate>(json, TemplateService.JsonOptions)!;

        Assert.Equal(300, back.Dpi);
        Assert.Equal(18, back.PrinterProfile.Darkness);
        Assert.Equal(4, back.PrinterProfile.SpeedIps);
        Assert.True(back.FieldDefinitions.Single(f => f.Name == "sku").Required);
        var be = Assert.IsType<BarcodeElement>(back.Elements[0]);
        Assert.Equal(90, be.Rotation);
        Assert.Equal(BarcodeFormatOption.GS1_128, be.Format);
        Assert.Equal(0.25, be.XDimensionMm);
        Assert.Equal(2, be.ErrorCorrectionLevel);
    }

    [Fact]
    public void V1Template_WithoutNewProps_BackfillsSafely()
    {
        // Simulate an old v1 .lbl: only Fields (names), no Dpi/PrinterProfile/FieldDefinitions.
        const string v1Json = """
        {
          "Version": 1,
          "Name": "Legacy",
          "WidthMm": 50, "HeightMm": 25,
          "Fields": ["itemName", "price"],
          "Elements": []
        }
        """;

        var t = JsonSerializer.Deserialize<LabelTemplate>(v1Json, TemplateService.JsonOptions)!;

        Assert.Equal(203, t.Dpi);                       // default backfilled
        Assert.NotNull(t.PrinterProfile);               // backfilled
        Assert.Equal(2, t.FieldDefinitions.Count);      // one per declared field
        Assert.Contains(t.FieldDefinitions, f => f.Name == "itemName");
        Assert.Contains(t.FieldDefinitions, f => f.Name == "price");
    }

    [Fact]
    public void Dpi_ClampsToSafeDefault()
    {
        var t = new LabelTemplate { Dpi = 5 };   // nonsense
        Assert.Equal(203, t.Dpi);                // setter rejects < 96
    }

    [Fact]
    public void BarcodeClone_CarriesRotationAndNewProps()
    {
        var be = new BarcodeElement { Rotation = 270, XDimensionMm = 0.5, QuietZoneMm = 2, ErrorCorrectionLevel = 3 };
        var clone = (BarcodeElement)be.Clone();
        Assert.Equal(270, clone.Rotation);
        Assert.Equal(0.5, clone.XDimensionMm);
        Assert.Equal(3, clone.ErrorCorrectionLevel);
        Assert.NotEqual(be.Id, clone.Id);   // clone gets a fresh id
    }
}
