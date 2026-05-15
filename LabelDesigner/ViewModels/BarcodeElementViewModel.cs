using LabelDesigner.Core.Models;
using LabelDesigner.Helpers;
using System.Windows.Media.Imaging;

namespace LabelDesigner.ViewModels;

public class BarcodeElementViewModel : ElementViewModelBase
{
    private string _barcodeValue = "12345678";
    private string? _boundField;
    private BarcodeFormatOption _format = BarcodeFormatOption.Code128;
    private bool _showText = true;

    public override ElementType ElementType => ElementType.Barcode;
    public override string DisplayName =>
        !string.IsNullOrEmpty(BoundField) ? $"Barcode [{BoundField}]" : $"Barcode \"{BarcodeValue}\"";

    public string BarcodeValue
    {
        get => _barcodeValue;
        set { if (Set(ref _barcodeValue, value)) OnPropertyChanged(nameof(BarcodeImage)); }
    }

    public string? BoundField { get => _boundField; set => Set(ref _boundField, value); }

    public BarcodeFormatOption Format
    {
        get => _format;
        set { if (Set(ref _format, value)) OnPropertyChanged(nameof(BarcodeImage)); }
    }

    public bool ShowText
    {
        get => _showText;
        set => Set(ref _showText, value);
    }

    /// <summary>Live preview image for the designer canvas.</summary>
    public BitmapSource? BarcodeImage =>
        BitmapHelper.GenerateBarcode(BarcodeValue, Format, (int)Math.Max(10, Width), (int)Math.Max(10, Height));

    /// <summary>Generates barcode with a specific value (used at print time for bound fields).</summary>
    public BitmapSource? GenerateWithValue(string value) =>
        BitmapHelper.GenerateBarcode(value, Format, (int)Math.Max(10, Width), (int)Math.Max(10, Height));

    public override LabelElement ToModel() => new BarcodeElement
    {
        Id = Id, X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        BarcodeValue = BarcodeValue, BoundField = BoundField, Format = Format, ShowText = ShowText
    };

    public override void FromModel(LabelElement element)
    {
        var m = (BarcodeElement)element;
        Id = m.Id; X = m.X; Y = m.Y; Width = m.Width; Height = m.Height; ZIndex = m.ZIndex;
        BarcodeValue = m.BarcodeValue; BoundField = m.BoundField; Format = m.Format; ShowText = m.ShowText;
    }
}
