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

        SetBinding(Canvas.LeftProperty,  new System.Windows.Data.Binding(nameof(ElementViewModelBase.X)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        SetBinding(Canvas.TopProperty,   new System.Windows.Data.Binding(nameof(ElementViewModelBase.Y)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        SetBinding(WidthProperty,        new System.Windows.Data.Binding(nameof(ElementViewModelBase.Width)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        SetBinding(HeightProperty,       new System.Windows.Data.Binding(nameof(ElementViewModelBase.Height)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        SetBinding(Panel.ZIndexProperty, new System.Windows.Data.Binding(nameof(ElementViewModelBase.ZIndex)));

        Cursor = System.Windows.Input.Cursors.SizeAll;
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
        _adorner.ResizeCompleted += (s, e) => ResizeCompleted?.Invoke(s, e);
        layer.Add(_adorner);
    }

    private void DetachAdorner()
    {
        if (_adorner == null) return;
        var layer = AdornerLayer.GetAdornerLayer(this);
        layer?.Remove(_adorner);
        _adorner = null;
    }
}
