using LabelDesigner.Core.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace LabelDesigner.ViewModels;

public class TextElementViewModel : ElementViewModelBase
{
    private string _text = "Text";
    private string _previewText = "Text";
    private string? _boundField;
    private string _fontFamily = "Arial";
    private double _fontSize = 12;
    private bool _bold;
    private bool _italic;
    private bool _underline;
    private string _color = "#000000";
    private TextAlignmentOption _alignment = TextAlignmentOption.Left;
    private Dictionary<string, string>? _liveFields;

    public override ElementType ElementType => ElementType.Text;
    protected override string TypeDisplayName =>
        !string.IsNullOrEmpty(BoundField) ? $"Text [{BoundField}]"
        : Text.Length > 18 ? $"Text \"{Text[..18]}…\"" : $"Text \"{Text}\"";

    public string Text
    {
        get => _text;
        set { if (Set(ref _text, value)) UpdatePreviewText(); }
    }

    /// <summary>
    /// What the canvas shows: substituted with live row data when loaded, otherwise equals Text.
    /// The canvas binds to this; the Properties Panel binds to Text for editing.
    /// </summary>
    public string PreviewText
    {
        get => _previewText;
        private set { if (Set(ref _previewText, value)) OnPropertyChanged(nameof(EffectiveFontSize)); }
    }

    public string? BoundField
    {
        get => _boundField;
        set { if (Set(ref _boundField, value)) UpdatePreviewText(); }
    }

    protected override string? BoundFieldValue { get => BoundField; set => BoundField = value; }

    public override void UpdatePreview(Dictionary<string, string>? fields)
    {
        _liveFields = fields;
        UpdatePreviewText();
    }

    private void UpdatePreviewText()
    {
        if (_liveFields == null) { PreviewText = Text; return; }

        if (!string.IsNullOrEmpty(BoundField) && _liveFields.TryGetValue(BoundField, out var bound))
        {
            PreviewText = bound;
            return;
        }

        // {{FieldName}} token substitution
        var result = Text;
        foreach (var (key, val) in _liveFields)
            result = result.Replace($"{{{{{key}}}}}", val, StringComparison.OrdinalIgnoreCase);
        PreviewText = result;
    }
    public string FontFamily
    {
        get => _fontFamily;
        set { if (Set(ref _fontFamily, value)) OnPropertyChanged(nameof(EffectiveFontSize)); }
    }

    public double FontSize
    {
        get => _fontSize;
        set { if (Set(ref _fontSize, Math.Max(6, value))) OnPropertyChanged(nameof(EffectiveFontSize)); }
    }

    public bool Bold
    {
        get => _bold;
        set
        {
            if (Set(ref _bold, value))
            {
                OnPropertyChanged(nameof(FontWeightValue));
                OnPropertyChanged(nameof(EffectiveFontSize));
            }
        }
    }

    public bool Italic
    {
        get => _italic;
        set
        {
            if (Set(ref _italic, value))
            {
                OnPropertyChanged(nameof(FontStyleValue));
                OnPropertyChanged(nameof(EffectiveFontSize));
            }
        }
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

    private bool _fitToBox;
    public bool FitToBox
    {
        get => _fitToBox;
        set
        {
            if (Set(ref _fitToBox, value))
            {
                OnPropertyChanged(nameof(StretchValue));
                OnPropertyChanged(nameof(FitViewboxVisibility));
                OnPropertyChanged(nameof(PlainTextVisibility));
                OnPropertyChanged(nameof(EffectiveFontSize));
            }
        }
    }

    private bool _multiLine;
    /// <summary>When true, text wraps onto multiple lines; when false (default), single-line.</summary>
    public bool MultiLine
    {
        get => _multiLine;
        set
        {
            if (Set(ref _multiLine, value))
            {
                OnPropertyChanged(nameof(TextWrappingValue));
                OnPropertyChanged(nameof(StretchValue));
                OnPropertyChanged(nameof(FitViewboxVisibility));
                OnPropertyChanged(nameof(PlainTextVisibility));
                OnPropertyChanged(nameof(EffectiveFontSize));
            }
        }
    }

    /// <summary>
    /// The canvas renders text through TWO visuals and shows exactly one — a Viewbox-scaled block
    /// for single-line FitToBox, and a plain block for everything else. A Viewbox measures its
    /// child with INFINITE width, so wrapping text inside one never wraps; the plain block is
    /// constrained by the element box and wraps exactly like the printed output (render parity).
    /// </summary>
    public System.Windows.Visibility FitViewboxVisibility =>
        (_fitToBox && !_multiLine) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility PlainTextVisibility =>
        (_fitToBox && !_multiLine) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    /// <summary>
    /// The font size the canvas actually renders at. For multi-line + FitToBox this is the largest
    /// size whose wrapped text fits the element box — the SAME binary search the print path runs
    /// (PrintService.BuildText), so the canvas resizes live as the user resizes the element instead
    /// of clipping. All other modes render at the declared FontSize.
    /// </summary>
    public double EffectiveFontSize
    {
        get
        {
            if (!_fitToBox || !_multiLine) return _fontSize;
            try { return ComputeFittedFontSize(); }
            catch { return _fontSize; }   // measurement needs the WPF text stack; fall back quietly
        }
    }

    private double ComputeFittedFontSize()
    {
        var tb = new System.Windows.Controls.TextBlock
        {
            Text         = PreviewText,
            FontFamily   = new FontFamily(_fontFamily),
            FontWeight   = FontWeightValue,
            FontStyle    = FontStyleValue,
            TextWrapping = TextWrapping.Wrap
        };
        double lo = 1, hi = _fontSize * 3 + 1, best = _fontSize;
        for (int iter = 0; iter < 16; iter++)
        {
            double mid = (lo + hi) / 2;
            tb.FontSize = mid;
            tb.Measure(new System.Windows.Size(Width, double.PositiveInfinity));
            if (tb.DesiredSize.Width <= Width && tb.DesiredSize.Height <= Height)
            { best = mid; lo = mid; }
            else hi = mid;
        }
        return best;
    }

    protected override void OnDimensionChanged()
    {
        base.OnDimensionChanged();
        OnPropertyChanged(nameof(EffectiveFontSize));   // re-fit when the user resizes the box
    }

    /// <summary>WPF TextWrapping derived from MultiLine.</summary>
    public TextWrapping TextWrappingValue => _multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap;

    /// <summary>
    /// Stretch mode for the canvas Viewbox wrapper.
    /// - FitToBox + single-line → Uniform: scales the text to fill the box.
    /// - Otherwise              → None:    text renders at the declared FontSize.
    /// Multi-line never uses Viewbox stretch because wrap inside a Viewbox is ill-defined
    /// (the Viewbox would measure with infinite width and never wrap).
    /// </summary>
    public System.Windows.Media.Stretch StretchValue =>
        (_fitToBox && !_multiLine) ? System.Windows.Media.Stretch.Uniform
                                   : System.Windows.Media.Stretch.None;

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
        LayerId = LayerId,
        BackgroundColor = BackgroundColor,
        Rotation = Rotation,
        Name = Name, IsLocked = IsLocked, GroupId = GroupId,
        Text = Text, BoundField = BoundField, FontFamily = FontFamily,
        FontSize = FontSize, Bold = Bold, Italic = Italic, Underline = Underline,
        Color = Color, Alignment = Alignment, FitToBox = FitToBox, MultiLine = MultiLine
    };

    public override void FromModel(LabelElement element)
    {
        var m = (TextElement)element;
        Id = m.Id; X = m.X; Y = m.Y; Width = m.Width; Height = m.Height; ZIndex = m.ZIndex;
        PrintCondition = m.PrintCondition;
        LayerId = m.LayerId;
        BackgroundColor = m.BackgroundColor;
        Rotation = m.Rotation;
        Name = m.Name; IsLocked = m.IsLocked; GroupId = m.GroupId;
        Text = m.Text; BoundField = m.BoundField; FontFamily = m.FontFamily;
        FontSize = m.FontSize; Bold = m.Bold; Italic = m.Italic; Underline = m.Underline;
        Color = m.Color; Alignment = m.Alignment; FitToBox = m.FitToBox; MultiLine = m.MultiLine;
    }
}
