using LabelDesigner.Core.Models;

namespace LabelDesigner.Core.Services;

/// <summary>
/// Builds the hardware-validation template set used to sign off a printer before production
/// cut-over (SOP "Hardware validation" section): thermal scan-test labels for the Zebra ZD621,
/// an Avery 5160 alignment sheet for the office laser, and a duplex front/back orientation pair.
/// Pure Core so the exact kit contents are unit-testable; the Designer saves the results into
/// the shared templates folder via <see cref="TemplateService"/>.
/// </summary>
public static class ValidationKit
{
    /// <summary>Every kit template name starts with this so they sort together and are
    /// obviously not production templates.</summary>
    public const string NamePrefix = "VALIDATE";

    public const string AlignmentDataFileName = "VALIDATE 5160 data.csv";
    public const string DuplexDataFileName    = "VALIDATE duplex data.csv";

    public const string Code128Value = "VG20260705";
    // GS1's published sample GTIN (09506000134352) + a lot — sample data, not a live product code.
    public const string Gs1Value     = "(01)09506000134352(10)VALID8";
    public const string QrValue      = "VANGO-QR-VALIDATE";

    public const string DuplexFrontName = NamePrefix + " Duplex Front";
    public const string DuplexBackName  = NamePrefix + " Duplex Back";

    /// <summary>
    /// Builds all kit templates. When <paramref name="dataDir"/> is given, the sheet templates get
    /// their <see cref="LabelTemplate.DefaultExcelPath"/> pointed at the kit CSVs inside it so the
    /// Print Station auto-loads the records.
    /// </summary>
    public static List<LabelTemplate> BuildTemplates(string? dataDir = null)
    {
        var list = new List<LabelTemplate>
        {
            ZebraScanTest("Code 128 GDI",  BarcodeFormatOption.Code128, Code128Value, zpl: false),
            ZebraScanTest("Code 128 ZPL",  BarcodeFormatOption.Code128, Code128Value, zpl: true),
            ZebraScanTest("GS1-128 GDI",   BarcodeFormatOption.GS1_128, Gs1Value,     zpl: false),
            ZebraQrTest(),
            Avery5160Alignment(dataDir),
            DuplexFront(dataDir),
            DuplexBack(dataDir),
        };
        return list;
    }

    /// <summary>30 rows (one per Avery 5160 cell), Cell in column A, Qty=1 in column B.</summary>
    public static string BuildAlignmentCsv() => BuildCellCsv(30);

    /// <summary>6 rows (one per 2×3 card cell).</summary>
    public static string BuildDuplexCsv() => BuildCellCsv(6);

    private static string BuildCellCsv(int rows)
    {
        var sb = new System.Text.StringBuilder("Cell,Qty\r\n");
        for (int i = 1; i <= rows; i++) sb.Append(i).Append(",1\r\n");
        return sb.ToString();
    }

    private static double Px(double mm) => LabelTemplate.MmToPixels(mm);

    // ── Zebra 2″×1″ scan-test labels ────────────────────────────────────────────
    // Layout (192×96 px canvas): caption / barcode with human-readable / expected-value line,
    // inside a border drawn exactly 2 mm in from every edge so the printed box doubles as a
    // dimensional check (must measure 46.8 × 21.4 mm).
    private static LabelTemplate ZebraScanTest(string variant, BarcodeFormatOption format,
        string value, bool zpl)
    {
        var t = new LabelTemplate
        {
            Name = $"{NamePrefix} Zebra {variant}",
            WidthMm = 50.8, HeightMm = 25.4, Dpi = 203,
            TestData = new() { },
            Elements =
            [
                DimensionBorder(50.8, 25.4),
                new TextElement { X=12, Y=9,  Width=168, Height=11, FontSize=7, Bold=true,
                    Alignment=TextAlignmentOption.Center,
                    Text=$"SCAN TEST — {variant} — 203 dpi", Name="Caption" },
                new BarcodeElement { X=26, Y=22, Width=140, Height=42, Format=format,
                    BarcodeValue=value, ShowText=true, TextFontSize=6,
                    XDimensionMm = format == BarcodeFormatOption.Code128 ? 0.25 : 0,
                    QuietZoneMm = 2.5, Name="Scan target" },
                new TextElement { X=12, Y=67, Width=168, Height=9, FontSize=6,
                    Alignment=TextAlignmentOption.Center,
                    Text=$"Scanner must read exactly: {value}", Name="Expected value" },
                new TextElement { X=12, Y=77, Width=168, Height=9, FontSize=5.5,
                    Alignment=TextAlignmentOption.Center,
                    Text="Border box must measure 46.8 × 21.4 mm", Name="Dimension note" },
            ]
        };
        if (zpl) t.PrinterProfile.OutputMode = PrintBackend.Zpl;
        return t;
    }

    private static LabelTemplate ZebraQrTest() => new()
    {
        Name = $"{NamePrefix} Zebra QR GDI",
        WidthMm = 50.8, HeightMm = 25.4, Dpi = 203,
        Elements =
        [
            DimensionBorder(50.8, 25.4),
            new TextElement { X=12, Y=9, Width=168, Height=11, FontSize=7, Bold=true,
                Alignment=TextAlignmentOption.Center, Text="SCAN TEST — QR (ECC M) — 203 dpi", Name="Caption" },
            new BarcodeElement { X=70, Y=22, Width=52, Height=52, Format=BarcodeFormatOption.QRCode,
                BarcodeValue=QrValue, ShowText=false, ErrorCorrectionLevel=1, QuietZoneMm=1.5,
                Name="Scan target" },
            new TextElement { X=12, Y=77, Width=168, Height=9, FontSize=6,
                Alignment=TextAlignmentOption.Center,
                Text=$"Scanner must read exactly: {QrValue}", Name="Expected value" },
        ]
    };

    /// <summary>Rectangle whose OUTER edge sits exactly 2 mm inside the label on every side —
    /// printed size must measure (W−4) × (H−4) mm, which catches DPI/scaling errors.</summary>
    private static ShapeElement DimensionBorder(double widthMm, double heightMm) => new()
    {
        ShapeType = ShapeType.Rectangle,
        X = Px(2), Y = Px(2), Width = Px(widthMm - 4), Height = Px(heightMm - 4),
        StrokeColor = "#000000", StrokeThickness = 1, FillColor = "Transparent",
        Name = "Dimension border", IsLocked = true, ZIndex = -10
    };

    // ── Avery 5160 alignment sheet ──────────────────────────────────────────────
    // 66.7 × 25.4 mm cell (252.3 × 96 px). A full-bleed border on every cell shows drift against
    // the die-cut edges; the cell number proves fill order; 30 CSV rows fill the sheet.
    private static LabelTemplate Avery5160Alignment(string? dataDir)
    {
        var t = new LabelTemplate
        {
            Name = $"{NamePrefix} 5160 Alignment",
            WidthMm = 66.7, HeightMm = 25.4,
            Page = PageLayout.Avery5160(),
            Fields = ["cellNum"],
            ExcelColumnMapping = new() { ["cellNum"] = "A" },
            PrintQtyColumn = "B",
            TestData = new() { ["cellNum"] = "1" },
            Elements =
            [
                new ShapeElement { ShapeType=ShapeType.Rectangle, X=0.5, Y=0.5,
                    Width=Px(66.7)-1, Height=Px(25.4)-1, StrokeColor="#000000", StrokeThickness=1,
                    FillColor="Transparent", Name="Cell border (must sit on die-cut edges)",
                    IsLocked=true, ZIndex=-10 },
                new TextElement { X=4,   Y=3,  Width=40,  Height=10, FontSize=6, Text="TL↖", Name="Top-left marker" },
                new TextElement { X=208, Y=83, Width=40,  Height=10, FontSize=6, Text="BR↘",
                    Alignment=TextAlignmentOption.Right, Name="Bottom-right marker" },
                new TextElement { X=76,  Y=26, Width=100, Height=42, FontSize=24, Bold=true,
                    Alignment=TextAlignmentOption.Center, Text="1", BoundField="cellNum", Name="Cell number" },
                new TextElement { X=20,  Y=72, Width=212, Height=10, FontSize=6,
                    Alignment=TextAlignmentOption.Center,
                    Text="Avery 5160 alignment — border must sit on the label edges", Name="Instruction" },
            ]
        };
        if (dataDir != null) t.DefaultExcelPath = Path.Combine(dataDir, AlignmentDataFileName);
        return t;
    }

    // ── Duplex orientation pair (2×3 cards, the flavor-card layout) ─────────────
    // 100 × 90 mm cards centred on Letter so margins are symmetric — required for the
    // mirrored-columns back to land behind its front after a long-edge flip.
    private static PageLayout CardPage() => new()
    {
        PageWidthMm = 215.9, PageHeightMm = 279.4,
        Rows = 3, Columns = 2,
        MarginLeftMm = (215.9 - 2 * 100.0) / 2,   // 7.95 — symmetric
        MarginTopMm  = (279.4 - 3 * 90.0) / 2,    // 4.7  — symmetric
        GutterXMm = 0, GutterYMm = 0
    };

    private static LabelTemplate DuplexFront(string? dataDir)
    {
        var page = CardPage();
        page.BackTemplateName = DuplexBackName;
        var t = DuplexCard(DuplexFrontName, "FRONT",
            "Hold the sheet to the light: this ↖ must sit exactly behind the BACK page's ↖ for the same cell.",
            page, dataDir);
        return t;
    }

    private static LabelTemplate DuplexBack(string? dataDir) =>
        DuplexCard(DuplexBackName, "BACK",
            "After a long-edge duplex flip this card must align with the FRONT card of the same cell.",
            CardPage(), dataDir);

    private static LabelTemplate DuplexCard(string name, string side, string note,
        PageLayout page, string? dataDir)
    {
        double w = Px(100), h = Px(90);
        var t = new LabelTemplate
        {
            Name = name,
            WidthMm = 100, HeightMm = 90,
            Page = page,
            Fields = ["cellNum"],
            ExcelColumnMapping = new() { ["cellNum"] = "A" },
            PrintQtyColumn = "B",
            TestData = new() { ["cellNum"] = "1" },
            Elements =
            [
                new ShapeElement { ShapeType=ShapeType.Rectangle, X=0.5, Y=0.5, Width=w-1, Height=h-1,
                    StrokeColor="#000000", StrokeThickness=1, FillColor="Transparent",
                    Name="Card border", IsLocked=true, ZIndex=-10 },
                new TextElement { X=8, Y=8, Width=120, Height=20, FontSize=12, Bold=true,
                    Text="↖ TOP-LEFT", Name="Corner marker" },
                new TextElement { X=w/2-80, Y=8, Width=160, Height=20, FontSize=12,
                    Alignment=TextAlignmentOption.Center, Text="▲ TOP EDGE", Name="Top marker" },
                new TextElement { X=20, Y=100, Width=w-40, Height=70, FontSize=44, Bold=true,
                    Alignment=TextAlignmentOption.Center, Text=side, Name="Side" },
                new TextElement { X=20, Y=180, Width=w-40, Height=44, FontSize=26,
                    Alignment=TextAlignmentOption.Center, Text="Cell {{cellNum}}", Name="Cell number" },
                new TextElement { X=16, Y=250, Width=w-32, Height=70, FontSize=9, MultiLine=true,
                    Alignment=TextAlignmentOption.Center, Text=note, Name="Instruction" },
            ]
        };
        if (dataDir != null) t.DefaultExcelPath = Path.Combine(dataDir, DuplexDataFileName);
        return t;
    }
}
