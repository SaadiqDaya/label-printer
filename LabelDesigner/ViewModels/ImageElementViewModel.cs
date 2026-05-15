using LabelDesigner.Core.Models;
using System.IO;
using System.Windows.Media.Imaging;

namespace LabelDesigner.ViewModels;

public class ImageElementViewModel : ElementViewModelBase
{
    private string _imagePath = "";
    private bool _maintainAspectRatio = true;
    private double _opacity = 1.0;

    public override ElementType ElementType => ElementType.Image;
    public override string DisplayName =>
        string.IsNullOrEmpty(ImagePath) ? "Image (empty)"
        : $"Image — {System.IO.Path.GetFileName(ImagePath)}";

    public string ImagePath
    {
        get => _imagePath;
        set { if (Set(ref _imagePath, value)) OnPropertyChanged(nameof(ImageSource)); }
    }

    public bool MaintainAspectRatio { get => _maintainAspectRatio; set => Set(ref _maintainAspectRatio, value); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0, 1)); }

    public BitmapImage? ImageSource
    {
        get
        {
            if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath)) return null;
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(ImagePath);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }
    }

    public override LabelElement ToModel() => new ImageElement
    {
        Id = Id, X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        PrintCondition = PrintCondition,
        ImagePath = ImagePath, MaintainAspectRatio = MaintainAspectRatio, Opacity = Opacity
    };

    public override void FromModel(LabelElement element)
    {
        var m = (ImageElement)element;
        Id = m.Id; X = m.X; Y = m.Y; Width = m.Width; Height = m.Height; ZIndex = m.ZIndex;
        PrintCondition = m.PrintCondition;
        ImagePath = m.ImagePath; MaintainAspectRatio = m.MaintainAspectRatio; Opacity = m.Opacity;
    }
}
