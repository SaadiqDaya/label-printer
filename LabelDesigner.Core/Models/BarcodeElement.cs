namespace LabelDesigner.Core.Models;

public class BarcodeElement : LabelElement
{
    public string BarcodeValue { get; set; } = "12345678";
    /// <summary>When set, this field name is substituted at print time (e.g. "barcode").</summary>
    public string? BoundField { get; set; }
    public BarcodeFormatOption Format { get; set; } = BarcodeFormatOption.Code128;
    public bool ShowText { get; set; } = true;
    public string TextFontFamily { get; set; } = "Arial";
    public double TextFontSize { get; set; } = 8;

    /// <summary>
    /// Narrow-bar (X-dimension) width in millimetres for 1D barcodes. 0 = auto-fit to the element box.
    /// When set, the renderer snaps the module width to whole printer dots for reliable scanning.
    /// </summary>
    public double XDimensionMm { get; set; } = 0;

    /// <summary>Quiet-zone (clear margin) width in millimetres applied each side. Standards want ~10× the X-dimension.</summary>
    public double QuietZoneMm { get; set; } = 2.5;

    /// <summary>Error-correction level for 2D symbologies (QR/Aztec): 0=L, 1=M, 2=Q, 3=H. Ignored by 1D codes.</summary>
    public int ErrorCorrectionLevel { get; set; } = 1;

    public override ElementType Type => ElementType.Barcode;

    public override LabelElement Clone() => new BarcodeElement
    {
        Id = Guid.NewGuid(), X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        PrintCondition = PrintCondition, LayerId = LayerId, BackgroundColor = BackgroundColor, Rotation = Rotation,
        BarcodeValue = BarcodeValue, BoundField = BoundField, Format = Format, ShowText = ShowText,
        TextFontFamily = TextFontFamily, TextFontSize = TextFontSize,
        XDimensionMm = XDimensionMm, QuietZoneMm = QuietZoneMm, ErrorCorrectionLevel = ErrorCorrectionLevel
    };
}
