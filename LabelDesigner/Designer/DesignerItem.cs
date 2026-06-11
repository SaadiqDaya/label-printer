using LabelDesigner.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LabelDesigner.Designer;

/// <summary>
/// Wrapper placed on the DesignerCanvas for each label element.
/// Handles selection highlight, resize adorner, and relays resize-completed events for undo.
/// </summary>
public class DesignerItem : ContentControl
{
    private Adorner? _adorner;   // ResizeAdorner for boxes, LineEndpointAdorner for lines

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(DesignerItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    public ElementViewModelBase ViewModel { get; }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Raised when the user finishes a resize drag, carrying before-state for undo.
    /// Subscribed to by DesignerCanvas.
    /// </summary>
    public event EventHandler<ResizeCompletedArgs>? ResizeCompleted;

    /// <summary>Raised when a LINE endpoint drag completes (lines use endpoint handles, not the
    /// resize box). Subscribed to by DesignerCanvas for undo.</summary>
    public event EventHandler<LineEndpointsChangedArgs>? LineEndpointsChanged;

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var item = (DesignerItem)d;
        if ((bool)e.NewValue && !item.ViewModel.IsLocked) item.AttachAdorner();
        else item.DetachAdorner();
    }

    public DesignerItem(ElementViewModelBase viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        // React to lock toggles: drop/show the resize handles and repaint the selection border.
        // No explicit detach needed — the item and its ViewModel reference each other and die together.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // OneWay: ViewModel is always the source of truth; TwoWay here can cause
        // an infinite PropertyChanged loop when NaN values enter (NaN != NaN breaks
        // the equality guard in Set<T>), resulting in a stack overflow.
        SetBinding(Canvas.LeftProperty,  new System.Windows.Data.Binding(nameof(ElementViewModelBase.X)));
        SetBinding(Canvas.TopProperty,   new System.Windows.Data.Binding(nameof(ElementViewModelBase.Y)));
        SetBinding(WidthProperty,        new System.Windows.Data.Binding(nameof(ElementViewModelBase.Width)));
        SetBinding(HeightProperty,       new System.Windows.Data.Binding(nameof(ElementViewModelBase.Height)));
        SetBinding(Panel.ZIndexProperty, new System.Windows.Data.Binding(nameof(ElementViewModelBase.EffectiveZIndex)));

        Cursor = System.Windows.Input.Cursors.SizeAll;
        SetBinding(OpacityProperty, new System.Windows.Data.Binding(nameof(ElementViewModelBase.DesignerOpacity)));
        SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(ElementViewModelBase.DesignerVisibility)));

        // Rotate about the element centre on the canvas, identical to PrintService (render parity).
        RenderTransformOrigin = new Point(0.5, 0.5);
        SetBinding(RenderTransformProperty, new System.Windows.Data.Binding(nameof(ElementViewModelBase.RotateTransform)));

        Background = Brushes.Transparent;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ElementViewModelBase.IsLocked))
        {
            if (IsSelected)
            {
                if (ViewModel.IsLocked) DetachAdorner();
                else AttachAdorner();
            }
            InvalidateVisual();
            return;
        }

        // Changing a shape between Line and box types swaps which adorner is appropriate.
        if (e.PropertyName == nameof(ShapeElementViewModel.ShapeType) && IsSelected)
        {
            DetachAdorner();
            if (!ViewModel.IsLocked) AttachAdorner();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (IsSelected)
        {
            // Locked elements show a grey border (no handles) so the operator sees why it won't move.
            var color = ViewModel.IsLocked ? Color.FromRgb(120, 120, 120) : Color.FromRgb(30, 144, 255);
            var pen = new Pen(new SolidColorBrush(color) { Opacity = 0.8 }, 1)
            {
                DashStyle = new DashStyle(new double[] { 4, 2 }, 0)
            };
            drawingContext.DrawRectangle(null, pen, new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsSelectedProperty) InvalidateVisual();
    }

    private void AttachAdorner()
    {
        if (_adorner != null) return;
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer == null) return;

        // Lines get endpoint handles (drag either end anywhere, all four quadrants);
        // every other element gets the 8-handle resize box.
        if (ViewModel is ShapeElementViewModel { ShapeType: Core.Models.ShapeType.Line })
        {
            var la = new LineEndpointAdorner(this);
            la.MoveCompleted += OnAdornerLineMoveCompleted;   // named handlers so DetachAdorner can -=
            _adorner = la;
        }
        else
        {
            var ra = new ResizeAdorner(this);
            ra.ResizeCompleted += OnAdornerResizeCompleted;
            _adorner = ra;
        }
        layer.Add(_adorner);
    }

    private void DetachAdorner()
    {
        if (_adorner == null) return;
        switch (_adorner)
        {
            case ResizeAdorner ra:       ra.ResizeCompleted -= OnAdornerResizeCompleted;   break;
            case LineEndpointAdorner la: la.MoveCompleted   -= OnAdornerLineMoveCompleted; break;
        }
        var layer = AdornerLayer.GetAdornerLayer(this);
        layer?.Remove(_adorner);
        _adorner = null;
    }

    private void OnAdornerResizeCompleted(object? sender, ResizeCompletedArgs e)
        => ResizeCompleted?.Invoke(sender, e);

    private void OnAdornerLineMoveCompleted(object? sender, LineEndpointsChangedArgs e)
        => LineEndpointsChanged?.Invoke(sender, e);
}
