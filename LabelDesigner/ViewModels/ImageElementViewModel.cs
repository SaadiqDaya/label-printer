using LabelDesigner.Core.Models;
using LabelDesigner.Helpers;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace LabelDesigner.ViewModels;

public class ImageElementViewModel : ElementViewModelBase
{
    private string _imagePath = "";
    private string? _boundField;
    private string? _imageBaseFolder;
    private bool _maintainAspectRatio = true;
    private double _opacity = 1.0;
    private ImageRenderMode _renderMode = ImageRenderMode.Color;
    private bool _invert;
    private int _threshold = 128;
    private Dictionary<string, string>? _liveFields;

    private string? _cachedKey;
    private BitmapSource? _cached;

    public static IEnumerable<ImageRenderMode> RenderModes => Enum.GetValues<ImageRenderMode>();

    public override ElementType ElementType => ElementType.Image;
    public override string DisplayName =>
        !string.IsNullOrEmpty(_boundField) ? $"Image [{_boundField}]"
        : string.IsNullOrEmpty(ImagePath) ? "Image (empty)"
        : $"Image — {System.IO.Path.GetFileName(ImagePath)}";

    public string ImagePath { get => _imagePath; set { if (Set(ref _imagePath, value)) { Invalidate(); OnPropertyChanged(nameof(DisplayName)); } } }
    public string? BoundField { get => _boundField; set { if (Set(ref _boundField, value)) { Invalidate(); OnPropertyChanged(nameof(DisplayName)); } } }
    public string? ImageBaseFolder { get => _imageBaseFolder; set { if (Set(ref _imageBaseFolder, value)) Invalidate(); } }
    public bool MaintainAspectRatio { get => _maintainAspectRatio; set => Set(ref _maintainAspectRatio, value); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0, 1)); }
    public ImageRenderMode RenderMode { get => _renderMode; set { if (Set(ref _renderMode, value)) Invalidate(); } }
    public bool Invert { get => _invert; set { if (Set(ref _invert, value)) Invalidate(); } }
    public int Threshold { get => _threshold; set { if (Set(ref _threshold, Math.Clamp(value, 0, 255))) Invalidate(); } }

    public override void UpdatePreview(Dictionary<string, string>? fields) { _liveFields = fields; Invalidate(); }

    private void Invalidate() { _cachedKey = null; OnPropertyChanged(nameof(ImageSource)); }

    /// <summary>Conditioned preview image (resolves the bound path when live data is present).</summary>
    public BitmapSource? ImageSource
    {
        get
        {
            var path = EffectivePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var key = $"{path}|{_renderMode}|{_invert}|{_threshold}";
            if (_cached != null && key == _cachedKey) return _cached;
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(path);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                _cached = BitmapHelper.ConditionImage(img, _renderMode, _invert, _threshold);
                _cachedKey = key;
                return _cached;
            }
            catch { return null; }
        }
    }

    private string EffectivePath()
    {
        if (!string.IsNullOrEmpty(_boundField) && _liveFields != null &&
            _liveFields.TryGetValue(_boundField, out var v) && !string.IsNullOrWhiteSpace(v))
            return !string.IsNullOrWhiteSpace(_imageBaseFolder) ? System.IO.Path.Combine(_imageBaseFolder, v) : v;
        return _imagePath;
    }

    public override LabelElement ToModel() => new ImageElement
    {
        Id = Id, X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        PrintCondition = PrintCondition, LayerId = LayerId, BackgroundColor = BackgroundColor, Rotation = Rotation,
        ImagePath = ImagePath, BoundField = BoundField, ImageBaseFolder = ImageBaseFolder,
        MaintainAspectRatio = MaintainAspectRatio, Opacity = Opacity,
        RenderMode = RenderMode, Invert = Invert, Threshold = Threshold
    };

    public override void FromModel(LabelElement element)
    {
        var m = (ImageElement)element;
        Id = m.Id; X = m.X; Y = m.Y; Width = m.Width; Height = m.Height; ZIndex = m.ZIndex;
        PrintCondition = m.PrintCondition; LayerId = m.LayerId; BackgroundColor = m.BackgroundColor; Rotation = m.Rotation;
        ImagePath = m.ImagePath; BoundField = m.BoundField; ImageBaseFolder = m.ImageBaseFolder;
        MaintainAspectRatio = m.MaintainAspectRatio; Opacity = m.Opacity;
        RenderMode = m.RenderMode; Invert = m.Invert; Threshold = m.Threshold;
    }
}
