using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Helpers;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace LabelDesigner.ViewModels;

public class BarcodeElementViewModel : ElementViewModelBase
{
    private string _barcodeValue = "12345678";
    private string? _boundField;
    private BarcodeFormatOption _format = BarcodeFormatOption.Code128;
    private bool _showText = false;
    private string _textFontFamily = "Arial";
    private double _textFontSize = 8;
    private double _xDimensionMm = 0;
    private double _quietZoneMm = 2.5;
    private int _errorCorrectionLevel = 1;
    private Dictionary<string, string>? _liveFields;
    private BitmapSource? _previewBarcodeImage;  // non-null when live data is active

    public override ElementType ElementType => ElementType.Barcode;
    protected override string TypeDisplayName =>
        !string.IsNullOrEmpty(BoundField) ? $"Barcode [{BoundField}]" : $"Barcode \"{BarcodeValue}\"";

    public string BarcodeValue
    {
        get => _barcodeValue;
        set { if (Set(ref _barcodeValue, value)) RefreshBarcodeImage(); }
    }

    public string? BoundField
    {
        get => _boundField;
        set { if (Set(ref _boundField, value)) RefreshBarcodeImage(); }
    }

    public BarcodeFormatOption Format
    {
        get => _format;
        set { if (Set(ref _format, value)) RefreshBarcodeImage(); }
    }

    public bool ShowText
    {
        get => _showText;
        set { if (Set(ref _showText, value)) RefreshBarcodeImage(); }
    }

    public string TextFontFamily
    {
        get => _textFontFamily;
        set { if (Set(ref _textFontFamily, value)) RefreshBarcodeImage(); }
    }

    public double TextFontSize
    {
        get => _textFontSize;
        set { if (Set(ref _textFontSize, Math.Max(6, value))) RefreshBarcodeImage(); }
    }

    /// <summary>Narrow-bar (X-dimension) width in mm for 1-D codes. 0 = auto-fit to the element box.</summary>
    public double XDimensionMm
    {
        get => _xDimensionMm;
        set { if (Set(ref _xDimensionMm, Math.Max(0, value))) RefreshBarcodeImage(); }
    }

    /// <summary>Quiet-zone width in mm applied each side (standards want ~10× the X-dimension).</summary>
    public double QuietZoneMm
    {
        get => _quietZoneMm;
        set { if (Set(ref _quietZoneMm, Math.Max(0, value))) RefreshBarcodeImage(); }
    }

    /// <summary>Error-correction level for 2-D codes (QR/Aztec): 0=L, 1=M, 2=Q, 3=H. Ignored by 1-D.</summary>
    public int ErrorCorrectionLevel
    {
        get => _errorCorrectionLevel;
        set { if (Set(ref _errorCorrectionLevel, Math.Clamp(value, 0, 3))) RefreshBarcodeImage(); }
    }

    /// <summary>Live design-time validation feedback (null = encodable). Bound in the Properties Panel
    /// so a bad value is caught while typing, not discovered at a downstream scanner.</summary>
    public string? ValidationMessage
    {
        get
        {
            var value = _barcodeValue;
            if (_liveFields != null && !string.IsNullOrEmpty(_boundField) &&
                _liveFields.TryGetValue(_boundField, out var v)) value = v;
            return BarcodeValidator.Validate(_format, value);
        }
    }

    public bool HasValidationError => ValidationMessage != null;

    /// <summary>
    /// Designer canvas preview image. Returns live-data barcode when a row is loaded,
    /// otherwise generates from BarcodeValue. Also refreshes when resized (via OnDimensionChanged).
    /// </summary>
    public BitmapSource? BarcodeImage => _previewBarcodeImage
        ?? BitmapHelper.GenerateBarcode(BarcodeValue, Format,
               (int)Math.Max(10, Width), (int)Math.Max(10, Height),
               ShowText, TextFontFamily, (float)TextFontSize, errorCorrection: _errorCorrectionLevel);

    /// <summary>Generates barcode with a specific value (used at print time for bound fields).</summary>
    public BitmapSource? GenerateWithValue(string value) =>
        BitmapHelper.GenerateBarcode(value, Format,
            (int)Math.Max(10, Width), (int)Math.Max(10, Height),
            ShowText, TextFontFamily, (float)TextFontSize, errorCorrection: _errorCorrectionLevel);

    protected override void OnDimensionChanged() => RefreshBarcodeImage();

    public override void UpdatePreview(Dictionary<string, string>? fields)
    {
        _liveFields = fields;
        if (fields == null)
        {
            _previewBarcodeImage = null;
            OnPropertyChanged(nameof(BarcodeImage));
            return;
        }
        RefreshBarcodeImage();
    }

    private void RefreshBarcodeImage()
    {
        if (_liveFields != null)
        {
            var effectiveValue = BarcodeValue;
            if (!string.IsNullOrEmpty(BoundField) && _liveFields.TryGetValue(BoundField, out var v))
                effectiveValue = v;
            _previewBarcodeImage = BitmapHelper.GenerateBarcode(
                effectiveValue, Format,
                (int)Math.Max(10, Width), (int)Math.Max(10, Height),
                ShowText, TextFontFamily, (float)TextFontSize, errorCorrection: _errorCorrectionLevel);
        }
        OnPropertyChanged(nameof(BarcodeImage));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationError));
    }

    public override LabelElement ToModel() => new BarcodeElement
    {
        Id = Id, X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        LayerId = LayerId,
        PrintCondition = PrintCondition,
        BackgroundColor = BackgroundColor,
        Rotation = Rotation,
        Name = Name, IsLocked = IsLocked, GroupId = GroupId,
        BarcodeValue = BarcodeValue, BoundField = BoundField, Format = Format,
        ShowText = ShowText, TextFontFamily = TextFontFamily, TextFontSize = TextFontSize,
        XDimensionMm = XDimensionMm, QuietZoneMm = QuietZoneMm, ErrorCorrectionLevel = ErrorCorrectionLevel
    };

    public override void FromModel(LabelElement element)
    {
        var m = (BarcodeElement)element;
        Id = m.Id; X = m.X; Y = m.Y; Width = m.Width; Height = m.Height; ZIndex = m.ZIndex;
        LayerId = m.LayerId;
        PrintCondition = m.PrintCondition;
        BackgroundColor = m.BackgroundColor;
        Rotation = m.Rotation;
        Name = m.Name; IsLocked = m.IsLocked; GroupId = m.GroupId;
        BarcodeValue = m.BarcodeValue; BoundField = m.BoundField; Format = m.Format;
        ShowText = m.ShowText; TextFontFamily = m.TextFontFamily; TextFontSize = m.TextFontSize;
        XDimensionMm = m.XDimensionMm; QuietZoneMm = m.QuietZoneMm; ErrorCorrectionLevel = m.ErrorCorrectionLevel;
    }
}
