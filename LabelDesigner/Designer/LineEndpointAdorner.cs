using LabelDesigner.ViewModels;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelDesigner.Designer;

/// <summary>Raised when a line endpoint drag completes, carrying before-state (incl. the diagonal
/// direction flag) for undo.</summary>
public class LineEndpointsChangedArgs(ShapeElementViewModel vm,
    double oldX, double oldY, double oldW, double oldH, bool oldReverseY) : EventArgs
{
    public ShapeElementViewModel Vm { get; } = vm;
    public double OldX { get; } = oldX;
    public double OldY { get; } = oldY;
    public double OldW { get; } = oldW;
    public double OldH { get; } = oldH;
    public bool OldReverseY { get; } = oldReverseY;
}

/// <summary>
/// Adorner for Line shapes: TWO endpoint handles instead of the 8-handle resize box. Dragging a
/// handle swings that end anywhere around the other (all four quadrants), updating X/Y/W/H and
/// LineReverseY exactly like the original draw gesture — so editing a line feels like drawing it.
/// </summary>
public class LineEndpointAdorner : Adorner
{
    private const double ThumbSize = 9;

    private readonly VisualCollection _visuals;
    private readonly DesignerItem _item;
    private readonly Thumb _p1, _p2;

    /// <summary>Canvas-space endpoints tracked during a drag (anchor stays put, moving follows).</summary>
    private Point _anchor, _moving;
    private (double X, double Y, double W, double H, bool Rev) _before;

    public event EventHandler<LineEndpointsChangedArgs>? MoveCompleted;

    private ShapeElementViewModel Vm => (ShapeElementViewModel)_item.ViewModel;

    public LineEndpointAdorner(DesignerItem adornedElement) : base(adornedElement)
    {
        _item = adornedElement;
        _visuals = new VisualCollection(this);
        _p1 = MakeThumb(first: true);
        _p2 = MakeThumb(first: false);
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    /// <summary>The line's two endpoints in CANVAS coordinates, honouring the diagonal direction.</summary>
    private (Point P1, Point P2) Endpoints()
    {
        var v = Vm;
        return v.LineReverseY
            ? (new Point(v.X, v.Y + v.Height), new Point(v.X + v.Width, v.Y))
            : (new Point(v.X, v.Y), new Point(v.X + v.Width, v.Y + v.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var v = Vm;
        var hs = ThumbSize / 2;
        // Local (item-relative) endpoint positions.
        double y1 = v.LineReverseY ? finalSize.Height : 0;
        double y2 = v.LineReverseY ? 0 : finalSize.Height;
        _p1.Arrange(new Rect(-hs, y1 - hs, ThumbSize, ThumbSize));
        _p2.Arrange(new Rect(finalSize.Width - hs, y2 - hs, ThumbSize, ThumbSize));
        return finalSize;
    }

    private Thumb MakeThumb(bool first)
    {
        var thumb = new Thumb
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Cursor = Cursors.Cross,
            Background = new SolidColorBrush(Color.FromRgb(30, 144, 255)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5)
        };

        thumb.DragStarted += (_, _) =>
        {
            var v = Vm;
            _before = (v.X, v.Y, v.Width, v.Height, v.LineReverseY);
            var (p1, p2) = Endpoints();
            _anchor = first ? p2 : p1;
            _moving = first ? p1 : p2;
        };

        thumb.DragDelta += (_, e) =>
        {
            if (Vm.IsLocked) return;
            _moving = new Point(Math.Max(0, _moving.X + e.HorizontalChange),
                                Math.Max(0, _moving.Y + e.VerticalChange));
            ApplyEndpoints(_anchor, _moving);
            InvalidateArrange();
        };

        thumb.DragCompleted += (_, _) =>
        {
            var v = Vm;
            if (v.X != _before.X || v.Y != _before.Y || v.Width != _before.W ||
                v.Height != _before.H || v.LineReverseY != _before.Rev)
                MoveCompleted?.Invoke(this, new LineEndpointsChangedArgs(
                    v, _before.X, _before.Y, _before.W, _before.H, _before.Rev));
        };

        _visuals.Add(thumb);
        return thumb;
    }

    /// <summary>Rebuilds the line's bounding box + direction from two free endpoints — the same rule
    /// the draw gesture uses, with the anchor playing the role of the drag start.</summary>
    private void ApplyEndpoints(Point a, Point m)
    {
        var v = Vm;
        double dx = m.X - a.X, dy = m.Y - a.Y;
        v.X = Math.Min(a.X, m.X);
        v.Y = Math.Min(a.Y, m.Y);
        v.Width = Math.Max(2, Math.Abs(dx));
        v.Height = Math.Max(1, Math.Abs(dy));
        // Anti-diagonal (up-right or down-left) renders bottom-left → top-right. Symmetric in a/m.
        v.LineReverseY = (dx > 0 && dy < 0) || (dx < 0 && dy > 0);
    }
}
