using LabelDesigner.Core.Models;
using System.Windows;
using System.Windows.Media;

namespace LabelDesigner.ViewModels;

public class TextElementViewModel : ElementViewModelBase
{
    private string _text = "Text";
    private string? _boundField;
    private string _fontFamily = "Arial";
    private double _fontSize = 12;
    private bool _bold;
    private bool _italic;
    private bool _underline;
    private string _color = "#000000";
    private TextAlignmentOption _alignment = TextAlignmentOption.Left;

    public override ElementType ElementType => ElementType.Text;
    public override string DisplayName =>
        !string.IsNullOrEmpty(BoundField) ? $"Text [{BoundField}]"
        : Text.Length > 18 ? $"Text \"{Text[..18]}…\"" : $"Text \"{Text}\"";

    public string Text { get => _text; set => Set(ref _text, value); }
    public string? BoundField { get => _boundField; set => Set(ref _boundField, value); }
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }

    public double FontSize
    {
        get => _fontSize;
        set => Set(ref _fontSize, Math.Max(6, value));
    }

    public bool Bold
    {
        get => _bold;
        set { if (Set(ref _bold, value)) OnPropertyChanged(nameof(FontWeightValue)); }
    }

    public bool Italic
    {
        get => _italic;
        set { if (Set(ref _italic, value)) OnPropertyChanged(nameof(FontStyleValue)); }
    }

    public bool Underline
    {
        get => _underline;
        set { if (Set(ref _underline, value)) OnPropertyChanged(nameof(TextDecorationsValue)); }
    }

    public string Color
    {
        get => _color;
        set { if (Set(ref _color, value)) OnPropertyChanged(nameof(ForegroundBrush)); }
    }

    public TextAlignmentOption Alignment
    {
        get => _alignment;
        set { if (Set(ref _alignment, value)) OnPropertyChanged(nameof(TextAlignmentValue)); }
    }

    // WPF-ready computed properties
    public FontWeight FontWeightValue => Bold ? FontWeights.Bold : FontWeights.Normal;
    public FontStyle FontStyleValue => Italic ? FontStyles.Italic : FontStyles.Normal;
    public TextDecorationCollection? TextDecorationsValue => Underline ? TextDecorations.Underline : null;
    public TextAlignment TextAlignmentValue => Alignment switch
    {
        TextAlignmentOption.Center => TextAlignment.Center,
        TextAlignmentOption.Right => TextAlignment.Right,
        _ => TextAlignment.Left
    };
    public Brush ForegroundBrush
    {
        get
        {
            try { return (Brush)new BrushConverter().ConvertFromString(Color)!; }
            catch { return Brushes.Black; }
        }
    }

    public override LabelElement ToModel() => new TextElement
    {
        Id = Id, X = X, Y = Y, Width = Width, Height = Height, ZIndex = ZIndex,
        PrintCondition = PrintCondition,
        Text = Text, BoundField = BoundField, FontFamily = FontFamily,
        FontSize = FontSize, Bold = Bold, Italic = Italic, Underline = Underline,
        Color = Color, Alignment = Alignment
    };

    public override void FromModel(LabelElement element)
    {
        var m = (TextElement)element;
        Id = m.Id; X = m.X; Y = m.Y; Width = m.Width; Height = m.Height; ZIndex = m.ZIndex;
        PrintCondition = m.PrintCondition;
        Text = m.Text; BoundField = m.BoundField; FontFamily = m.FontFamily;
        FontSize = m.FontSize; Bold = m.Bold; Italic = m.Italic; Underline = m.Underline;
        Color = m.Color; Alignment = m.Alignment;
    }
}
