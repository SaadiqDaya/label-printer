namespace LabelDesigner.Core.Models;

/// <summary>How an image is conditioned for thermal print. Color = untouched; Grayscale; Threshold =
/// hard black/white at a cutoff; Dither = Floyd-Steinberg (best for photos/logos on mono thermal).</summary>
public enum ImageRenderMode { Color, Grayscale, Threshold, Dither }

public class ImageElement : LabelElement
{
    /// <summary>Static image path (used when BoundField is empty).</summary>
    public string ImagePath { get; set; } = "";

    /// <summary>When set, the image PATH comes from this field at print time (data-driven images).</summary>
    public string? BoundField { get; set; }

    /// <summary>Optional folder the bound value is combined with, e.g. base "C:\icons" + field "milk.png".</summary>
    public string? ImageBaseFolder { get; set; }

    public bool MaintainAspectRatio { get; set; } = true;

    // ── Thermal conditioning ──
    public ImageRenderMode RenderMode { get; set; } = ImageRenderMode.Color;
    public bool Invert { get; set; }
    /// <summary>0..255 cutoff used by Threshold mode.</summary>
    public int Threshold { get; set; } = 128;

    private double _opacity = 1.0;
    /// <summary>Image opacity, clamped to [0,1].</summary>
    public double Opacity
    {
        get => _opacity;
        set => _opacity = Math.Clamp(value, 0.0, 1.0);
    }

    public override ElementType Type => ElementType.Image;

    public override LabelElement Clone() => new ImageElement
    {
        Id = Guid.NewGuid(), X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        PrintCondition = PrintCondition, LayerId = LayerId, BackgroundColor = BackgroundColor, Rotation = Rotation,
        Name = Name, IsLocked = IsLocked,   // GroupId intentionally NOT copied: a clone shouldn't silently join the group
        ImagePath = ImagePath, BoundField = BoundField, ImageBaseFolder = ImageBaseFolder,
        MaintainAspectRatio = MaintainAspectRatio, Opacity = Opacity,
        RenderMode = RenderMode, Invert = Invert, Threshold = Threshold
    };
}
