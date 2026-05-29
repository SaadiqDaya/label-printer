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
    private ResizeAdorner? _adorner;

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

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var item = (DesignerItem)d;
        if ((bool)e.NewValue) item.AttachAdorner();
        else item.DetachAdorner();
    }

    public DesignerItem(ElementViewModelBase viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (IsSelected)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(30, 144, 255)) { Opacity = 0.8 }, 1)
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
        _adorner = new ResizeAdorner(this);
        // Named handler instead of a lambda so we can actually -= it in DetachAdorner.
        _adorner.ResizeCompleted += OnAdornerResizeCompleted;
        layer.Add(_adorner);
    }

    private void DetachAdorner()
    {
        if (_adorner == null) return;
        _adorner.ResizeCompleted -= OnAdornerResizeCompleted;
        var layer = AdornerLayer.GetAdornerLayer(this);
        layer?.Remove(_adorner);
        _adorner = null;
    }

    private void OnAdornerResizeCompleted(object? sender, ResizeCompletedArgs e)
        => ResizeCompleted?.Invoke(sender, e);
}
