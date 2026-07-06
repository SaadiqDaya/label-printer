using System.IO;
using System.Text.Json;
using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using Xunit;

namespace LabelDesigner.Tests;

public class ValidationKitTests
{
    [Fact]
    public void BuildsAllSevenTemplates_AllPrefixed()
    {
        var list = ValidationKit.BuildTemplates();
        Assert.Equal(7, list.Count);
        Assert.All(list, t => Assert.StartsWith(ValidationKit.NamePrefix, t.Name));
        Assert.Equal(list.Count, list.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryKitBarcode_PassesTheSymbologyValidator()
    {
        foreach (var t in ValidationKit.BuildTemplates())
            foreach (var b in t.Elements.OfType<BarcodeElement>())
                Assert.Null(BarcodeValidator.Validate(b.Format, b.BarcodeValue));
    }

    [Fact]
    public void ZebraScanTests_Are2x1At203_WithZplVariantOptedIn()
    {
        var list = ValidationKit.BuildTemplates();
        var zebra = list.Where(t => t.Name.Contains("Zebra")).ToList();
        Assert.Equal(4, zebra.Count);
        Assert.All(zebra, t =>
        {
            Assert.Equal(50.8, t.WidthMm);
            Assert.Equal(25.4, t.HeightMm);
            Assert.Equal(203, t.Dpi);
        });
        // Exactly one ZPL variant; everything else stays on the safe GDI default.
        Assert.Single(zebra, t => t.PrinterProfile.OutputMode == PrintBackend.Zpl);
        Assert.Contains(zebra, t => t.Elements.OfType<BarcodeElement>()
            .Any(b => b.Format == BarcodeFormatOption.GS1_128));
        Assert.Contains(zebra, t => t.Elements.OfType<BarcodeElement>()
            .Any(b => b.Format == BarcodeFormatOption.QRCode));
    }

    [Fact]
    public void DimensionBorder_SitsExactly2mmInside()
    {
        var code128 = ValidationKit.BuildTemplates().First(t => t.Name.Contains("Code 128 GDI"));
        var border = code128.Elements.OfType<ShapeElement>().Single();
        Assert.Equal(LabelTemplate.MmToPixels(2), border.X, 3);
        Assert.Equal(LabelTemplate.MmToPixels(2), border.Y, 3);
        Assert.Equal(LabelTemplate.MmToPixels(46.8), border.Width, 3);
        Assert.Equal(LabelTemplate.MmToPixels(21.4), border.Height, 3);
    }

    [Fact]
    public void AlignmentSheet_MatchesAvery5160GridAndMapping()
    {
        var t = ValidationKit.BuildTemplates().First(x => x.Name.Contains("5160"));
        Assert.NotNull(t.Page);
        Assert.True(t.Page!.SameGridAs(PageLayout.Avery5160()));
        Assert.Equal(66.7, t.WidthMm);
        Assert.Equal(25.4, t.HeightMm);
        Assert.Equal("A", t.ExcelColumnMapping["cellNum"]);
        Assert.Equal("B", t.PrintQtyColumn);
    }

    [Fact]
    public void DuplexPair_LinksBackAndHasSymmetricMargins()
    {
        var list  = ValidationKit.BuildTemplates();
        var front = list.First(t => t.Name == ValidationKit.DuplexFrontName);
        var back  = list.First(t => t.Name == ValidationKit.DuplexBackName);

        Assert.Equal(ValidationKit.DuplexBackName, front.Page!.BackTemplateName);
        Assert.Equal("", back.Page!.BackTemplateName);
        Assert.Equal(front.WidthMm, back.WidthMm);
        Assert.Equal(front.HeightMm, back.HeightMm);
        Assert.True(front.Page.SameGridAs(back.Page));

        // Symmetric margins are REQUIRED for mirrored-column duplex to line up after a long-edge flip.
        var p = front.Page;
        double rightMargin  = p.PageWidthMm  - p.MarginLeftMm - p.Columns * front.WidthMm  - (p.Columns - 1) * p.GutterXMm;
        double bottomMargin = p.PageHeightMm - p.MarginTopMm  - p.Rows    * front.HeightMm - (p.Rows    - 1) * p.GutterYMm;
        Assert.Equal(p.MarginLeftMm, rightMargin, 3);
        Assert.Equal(p.MarginTopMm,  bottomMargin, 3);
    }

    [Fact]
    public void Csvs_ParseToExpectedRowCounts_WithCellAndQty()
    {
        var align = Services.CsvImportService.ParseGeneric(ValidationKit.BuildAlignmentCsv());
        Assert.Equal(30, align.Count);
        Assert.Equal("1", align[0]["Cell"]);
        Assert.Equal("30", align[^1]["Cell"]);
        Assert.All(align, r => Assert.Equal("1", r["Qty"]));

        var duplex = Services.CsvImportService.ParseGeneric(ValidationKit.BuildDuplexCsv());
        Assert.Equal(6, duplex.Count);
    }

    [Fact]
    public void DataDir_SetsDefaultExcelPathOnSheetTemplates()
    {
        var dir  = Path.Combine(Path.GetTempPath(), "vkit-test");
        var list = ValidationKit.BuildTemplates(dir);
        var align = list.First(t => t.Name.Contains("5160"));
        Assert.Equal(Path.Combine(dir, ValidationKit.AlignmentDataFileName), align.DefaultExcelPath);
        var front = list.First(t => t.Name == ValidationKit.DuplexFrontName);
        Assert.Equal(Path.Combine(dir, ValidationKit.DuplexDataFileName), front.DefaultExcelPath);
        // Zebra labels have no data file.
        Assert.Null(list.First(t => t.Name.Contains("Code 128 GDI")).DefaultExcelPath);
    }

    [Fact]
    public void EveryKitTemplate_RendersThroughTheRealPrintPipeline()
    {
        // RenderPreview is fail-loud: an un-encodable barcode (bad GS1 AIs, X-dim that can't fit…)
        // throws instead of printing blank — so a successful render proves the kit's payloads
        // actually encode through ZXing at template DPI.
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var t in ValidationKit.BuildTemplates())
                {
                    var fields = new Dictionary<string, string>(t.TestData, StringComparer.OrdinalIgnoreCase);
                    var bmp = Services.PrintService.RenderPreview(t, fields, dpi: t.Dpi);
                    Assert.True(bmp.PixelWidth > 0 && bmp.PixelHeight > 0, t.Name);
                }
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    [Fact]
    public void KitTemplates_SurviveJsonRoundTrip()
    {
        foreach (var t in ValidationKit.BuildTemplates())
        {
            var json = JsonSerializer.Serialize(t, TemplateService.JsonOptions);
            var back = JsonSerializer.Deserialize<LabelTemplate>(json, TemplateService.JsonOptions)!;
            Assert.Equal(t.Name, back.Name);
            Assert.Equal(t.WidthMm, back.WidthMm);
            Assert.Equal(t.HeightMm, back.HeightMm);
            Assert.Equal(t.Elements.Count, back.Elements.Count);
            Assert.Equal(t.Page != null, back.Page != null);
        }
    }
}
