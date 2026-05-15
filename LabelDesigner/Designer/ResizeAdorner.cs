using LabelDesigner.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelDesigner.Designer;

/// <summary>
/// Adorner that draws 8 resize handles around the selected DesignerItem.
/// Dragging a handle updates the element ViewModel's X/Y/Width/Height.
/// Fires <see cref="ResizeCompleted"/> when a drag ends with a net change,
/// so the canvas can push an undo action.
/// </summary>
public class ResizeAdorner : Adorner
{
    private readonly VisualCollection _visuals;
    private readonly DesignerItem _item;

    private readonly Thumb _nw, _n, _ne, _e, _se, _s, _sw, _w;
    private const double ThumbSize = 8;

    public event EventHandler<ResizeCompletedArgs>? ResizeCompleted;

    public ResizeAdorner(DesignerItem adornedElement) : base(adornedElement)
    {
        _item = adornedElement;
        _visuals = new VisualCollection(this);

        _nw = MakeThumb(Cursors.SizeNWSE, (dx, dy) => ResizeTopLeft(dx, dy));
        _n  = MakeThumb(Cursors.SizeNS,   (dx, dy) => ResizeTop(dy));
        _ne = MakeThumb(Cursors.SizeNESW, (dx, dy) => ResizeTopRight(dx, dy));
        _e  = MakeThumb(Cursors.SizeWE,   (dx, dy) => ResizeRight(dx));
        _se = MakeThumb(Cursors.SizeNWSE, (dx, dy) => ResizeBottomRight(dx, dy));
        _s  = MakeThumb(Cursors.SizeNS,   (dx, dy) => ResizeBottom(dy));
        _sw = MakeThumb(Cursors.SizeNESW, (dx, dy) => ResizeBottomLeft(dx, dy));
        _w  = MakeThumb(Cursors.SizeWE,   (dx, dy) => ResizeLeft(dx));
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size ArrangeOverride(Size finalSize)
    {
        var w  = finalSize.Width;
        var h  = finalSize.Height;
        var hs = ThumbSize / 2;

        _nw.Arrange(Rect(-hs,        -hs));
        _n.Arrange( Rect(w / 2 - hs, -hs));
        _ne.Arrange(Rect(w - hs,     -hs));
        _e.Arrange( Rect(w - hs,     h / 2 - hs));
        _se.Arrange(Rect(w - hs,     h - hs));
        _s.Arrange( Rect(w / 2 - hs, h - hs));
        _sw.Arrange(Rect(-hs,        h - hs));
        _w.Arrange( Rect(-hs,        h / 2 - hs));

        return finalSize;
    }

    private static Rect Rect(double x, double y) => new(x, y, ThumbSize, ThumbSize);

    private Thumb MakeThumb(Cursor cursor, Action<double, double> dragAction)
    {
        var thumb = new Thumb
        {
            Width = ThumbSize, Height = ThumbSize,
            Cursor = cursor,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(30, 144, 255)),
            BorderThickness = new Thickness(1)
        };

        double bx = 0, by = 0, bw = 0, bh = 0;
        thumb.DragStarted   += (_, _) => { bx = Vm.X; by = Vm.Y; bw = Vm.Width; bh = Vm.Height; };
        thumb.DragDelta     += (_, e) => dragAction(e.HorizontalChange, e.VerticalChange);
        thumb.DragCompleted += (_, _) =>
        {
            if (Vm.X != bx || Vm.Y != by || Vm.Width != bw || Vm.Height != bh)
                ResizeCompleted?.Invoke(this, new ResizeCompletedArgs(Vm, bx, by, bw, bh));
        };

        _visuals.Add(thumb);
        return thumb;
    }

    private ElementViewModelBase Vm => _item.ViewModel;

    private void ResizeTopLeft(double dx, double dy)
    {
        var newW = Vm.Width - dx; var newH = Vm.Height - dy;
        if (newW >= 10) { Vm.X += dx; Vm.Width = newW; }
        if (newH >= 8)  { Vm.Y += dy; Vm.Height = newH; }
    }

    private void ResizeTop(double dy)
    {
        var newH = Vm.Height - dy;
        if (newH >= 8) { Vm.Y += dy; Vm.Height = newH; }
    }

    private void ResizeTopRight(double dx, double dy)
    {
        var newW = Vm.Width + dx; var newH = Vm.Height - dy;
        if (newW >= 10) Vm.Width = newW;
        if (newH >= 8)  { Vm.Y += dy; Vm.Height = newH; }
    }

    private void ResizeRight(double dx) { var n = Vm.Width + dx; if (n >= 10) Vm.Width = n; }

    private void ResizeBottomRight(double dx, double dy)
    {
        var newW = Vm.Width + dx; var newH = Vm.Height + dy;
        if (newW >= 10) Vm.Width = newW;
        if (newH >= 8)  Vm.Height = newH;
    }

    private void ResizeBottom(double dy) { var n = Vm.Height + dy; if (n >= 8) Vm.Height = n; }

    private void ResizeBottomLeft(double dx, double dy)
    {
        var newW = Vm.Width - dx; var newH = Vm.Height + dy;
        if (newW >= 10) { Vm.X += dx; Vm.Width = newW; }
        if (newH >= 8)  Vm.Height = newH;
    }

    private void ResizeLeft(double dx) { var n = Vm.Width - dx; if (n >= 10) { Vm.X += dx; Vm.Width = n; } }
}
