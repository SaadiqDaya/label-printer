namespace LabelDesigner.Core.Models;

public class BarcodeElement : LabelElement
{
    public string BarcodeValue { get; set; } = "12345678";
    /// <summary>When set, this field name is substituted at print time (e.g. "barcode").</summary>
    public string? BoundField { get; set; }
    public BarcodeFormatOption Format { get; set; } = BarcodeFormatOption.Code128;
    public bool ShowText { get; set; } = true;

    public override ElementType Type => ElementType.Barcode;

    public override LabelElement Clone() => new BarcodeElement
    {
        Id = Guid.NewGuid(), X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        BarcodeValue = BarcodeValue, BoundField = BoundField, Format = Format, ShowText = ShowText
    };
}
