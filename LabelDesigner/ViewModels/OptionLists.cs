using LabelDesigner.Core.Models;

namespace LabelDesigner.ViewModels;

/// <summary>Static collections for XAML ComboBox ItemsSources.</summary>
public static class AlignmentOptions
{
    public static readonly IReadOnlyList<TextAlignmentOption> All =
    [
        TextAlignmentOption.Left,
        TextAlignmentOption.Center,
        TextAlignmentOption.Right
    ];
}

public static class BarcodeFormatOptions
{
    public static readonly IReadOnlyList<BarcodeFormatOption> All =
    [
        BarcodeFormatOption.Code128,
        BarcodeFormatOption.QRCode,
        BarcodeFormatOption.EAN13,
        BarcodeFormatOption.UPCA,
        BarcodeFormatOption.DataMatrix,
        BarcodeFormatOption.PDF417
    ];
}

public static class ShapeTypeOptions
{
    public static readonly IReadOnlyList<ShapeType> All =
    [
        ShapeType.Rectangle,
        ShapeType.Ellipse,
        ShapeType.Line
    ];
}
