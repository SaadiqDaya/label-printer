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
        BarcodeFormatOption.GS1_128,
        BarcodeFormatOption.Code39,
        BarcodeFormatOption.Code93,
        BarcodeFormatOption.ITF,
        BarcodeFormatOption.Codabar,
        BarcodeFormatOption.EAN13,
        BarcodeFormatOption.UPCA,
        BarcodeFormatOption.QRCode,
        BarcodeFormatOption.DataMatrix,
        BarcodeFormatOption.PDF417,
        BarcodeFormatOption.Aztec
    ];

    /// <summary>True for 2-D symbologies (no human-readable text line; error-correction applies).</summary>
    public static bool Is2D(BarcodeFormatOption f) =>
        f is BarcodeFormatOption.QRCode or BarcodeFormatOption.DataMatrix
          or BarcodeFormatOption.PDF417 or BarcodeFormatOption.Aztec;
}

public static class ShapeTypeOptions
{
    public static readonly IReadOnlyList<ShapeType> All =
    [
        ShapeType.Rectangle,
        ShapeType.Ellipse,
        ShapeType.Line,
        ShapeType.Triangle,
        ShapeType.Arrow,
        ShapeType.Diamond
    ];
}
