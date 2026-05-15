namespace LabelDesigner.Core.Models;

public class ImageElement : LabelElement
{
    public string ImagePath { get; set; } = "";
    public bool MaintainAspectRatio { get; set; } = true;
    public double Opacity { get; set; } = 1.0;

    public override ElementType Type => ElementType.Image;

    public override LabelElement Clone() => new ImageElement
    {
        Id = Guid.NewGuid(), X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        PrintCondition = PrintCondition,
        ImagePath = ImagePath, MaintainAspectRatio = MaintainAspectRatio, Opacity = Opacity
    };
}
