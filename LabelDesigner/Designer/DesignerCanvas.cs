using LabelDesigner.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LabelDesigner.Designer;

public enum DesignerTool { Select, AddText, AddBarcode, AddImage, AddRectangle }

/// <summary>
/// The label design surface. Manages DesignerItems, selection, drag-to-move,
/// arrow-key nudge, snap-to-grid, and undo recording for moves and resizes.
/// </summary>
public class DesignerCanvas : Canvas
{
    private DesignerItem? _selectedItem;
    private Point _dragStartMouse;
    private Point _dragStartElement;
    private bool _isDragging;

    public event EventHandler<ElementViewModelBase?>? SelectionChanged;

    public DesignerTool CurrentTool { get; set; } = DesignerTool.Select;

    public DesignerItem? SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (_selectedItem == value) return;
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
                _selectedItem.ViewModel.IsSelected = false;
            }
            _selectedItem = value;
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = true;
                _selectedItem.ViewModel.IsSelected = true;
            }
            SelectionChanged?.Invoke(this, _selectedItem?.ViewModel);
        }
    }

    public void AddElement(ElementViewModelBase viewModel)
    {
        var item = CreateItem(viewModel);
        Children.Add(item);
        SelectedItem = item;
    }

    public void ClearAll()
    {
        SelectedItem = null;
        Children.Clear();
    }

    public IEnumerable<ElementViewModelBase> GetAllViewModels() =>
        Children.OfType<DesignerItem>().Select(i => i.ViewModel);

    public void RemoveByViewModel(ElementViewModelBase viewModel)
    {
        var item = Children.OfType<DesignerItem>().FirstOrDefault(i => i.ViewModel == viewModel);
        if (item == null) return;
        if (SelectedItem == item) SelectedItem = null;
        Children.Remove(item);
    }

    // ─── Canvas mouse events ───────────────────────────────────────────────
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Source == this)
        {
            SelectedItem = null;
            Focus();
        }
    }

    // ─── Keyboard ─────────────────────────────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Escape: deselect
        if (e.Key == Key.Escape) { SelectedItem = null; e.Handled = true; return; }

        // Arrow key nudge for selected element
        if (_selectedItem != null && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            var vm = _selectedItem.ViewModel;
            var oldX = vm.X; var oldY = vm.Y;

            if (e.Key == Key.Left)  vm.X = Math.Max(0, vm.X - step);
            if (e.Key == Key.Right) vm.X = vm.X + step;
            if (e.Key == Key.Up)    vm.Y = Math.Max(0, vm.Y - step);
            if (e.Key == Key.Down)  vm.Y = vm.Y + step;

            if ((vm.X != oldX || vm.Y != oldY) && DataContext is DesignerViewModel designer)
                designer.UndoManager.Push(new MoveAction(vm, oldX, oldY, vm.X, vm.Y));

            e.Handled = true;
        }

        // Note: Delete key is handled by the MainWindow KeyBinding → DeleteSelectedCommand
        //       which goes through DesignerViewModel.DeleteSelected() and records undo.
        //       Do NOT handle Delete here to avoid double-removal.
    }

    // ─── Item creation ─────────────────────────────────────────────────────
    private DesignerItem CreateItem(ElementViewModelBase viewModel)
    {
        var item = new DesignerItem(viewModel);

        item.Content = viewModel switch
        {
            TextElementViewModel    tv => BuildTextView(tv),
            BarcodeElementViewModel bv => BuildBarcodeView(bv),
            ImageElementViewModel   iv => BuildImageView(iv),
            ShapeElementViewModel   sv => BuildShapeView(sv),
            _                         => new TextBlock { Text = "?" }
        };

        item.MouseDown += Item_MouseDown;
        item.MouseMove += Item_MouseMove;
        item.MouseUp   += Item_MouseUp;

        // Hook resize-completed to push undo action
        item.ResizeCompleted += (_, args) =>
        {
            if (DataContext is DesignerViewModel designer)
                designer.UndoManager.Push(new ResizeAction(
                    args.Vm,
                    args.OldX, args.OldY, args.OldW, args.OldH,
                    args.Vm.X, args.Vm.Y, args.Vm.Width, args.Vm.Height));
        };

        Focusable = true;
        return item;
    }

    // ─── Drag-to-move ──────────────────────────────────────────────────────
    private void Item_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = (DesignerItem)sender;
        SelectedItem = item;
        _dragStartMouse   = e.GetPosition(this);
        _dragStartElement = new Point(item.ViewModel.X, item.ViewModel.Y);
        _isDragging = true;
        item.CaptureMouse();
        Focus();
        e.Handled = true;
    }

    private void Item_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _selectedItem == null) return;

        var pos = e.GetPosition(this);
        var newX = Math.Max(0, _dragStartElement.X + pos.X - _dragStartMouse.X);
        var newY = Math.Max(0, _dragStartElement.Y + pos.Y - _dragStartMouse.Y);

        if (DataContext is DesignerViewModel vm && vm.SnapToGrid && vm.GridSize > 0)
        {
            var g = vm.GridSize;
            newX = Math.Round(newX / g) * g;
            newY = Math.Round(newY / g) * g;
        }

        _selectedItem.ViewModel.X = newX;
        _selectedItem.ViewModel.Y = newY;
    }

    private void Item_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging || _selectedItem == null) return;

        var vm = _selectedItem.ViewModel;
        if ((vm.X != _dragStartElement.X || vm.Y != _dragStartElement.Y)
            && DataContext is DesignerViewModel designer)
        {
            designer.UndoManager.Push(new MoveAction(
                vm, _dragStartElement.X, _dragStartElement.Y, vm.X, vm.Y));
        }

        ((DesignerItem)sender).ReleaseMouseCapture();
        _isDragging = false;
    }

    // ─── View builders ──────────────────────────────────────────────────────
    private static System.Windows.Controls.TextBlock BuildTextView(TextElementViewModel vm)
    {
        var tb = new System.Windows.Controls.TextBlock { TextWrapping = TextWrapping.Wrap };
        tb.SetBinding(System.Windows.Controls.TextBlock.TextProperty,        new System.Windows.Data.Binding(nameof(TextElementViewModel.Text)));
        tb.SetBinding(System.Windows.Controls.TextBlock.FontFamilyProperty,  new System.Windows.Data.Binding(nameof(TextElementViewModel.FontFamily)) { Converter = new Converters.StringToFontFamilyConverter() });
        tb.SetBinding(System.Windows.Controls.TextBlock.FontSizeProperty,    new System.Windows.Data.Binding(nameof(TextElementViewModel.FontSize)));
        tb.SetBinding(System.Windows.Controls.TextBlock.FontWeightProperty,  new System.Windows.Data.Binding(nameof(TextElementViewModel.FontWeightValue)));
        tb.SetBinding(System.Windows.Controls.TextBlock.FontStyleProperty,   new System.Windows.Data.Binding(nameof(TextElementViewModel.FontStyleValue)));
        tb.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty,  new System.Windows.Data.Binding(nameof(TextElementViewModel.ForegroundBrush)));
        tb.SetBinding(System.Windows.Controls.TextBlock.TextAlignmentProperty, new System.Windows.Data.Binding(nameof(TextElementViewModel.TextAlignmentValue)));
        tb.DataContext = vm;
        return tb;
    }

    private static System.Windows.Controls.Image BuildBarcodeView(BarcodeElementViewModel vm)
    {
        var img = new System.Windows.Controls.Image { Stretch = System.Windows.Media.Stretch.Fill };
        img.SetBinding(System.Windows.Controls.Image.SourceProperty, new System.Windows.Data.Binding(nameof(BarcodeElementViewModel.BarcodeImage)));
        img.DataContext = vm;
        return img;
    }

    private static System.Windows.Controls.Image BuildImageView(ImageElementViewModel vm)
    {
        var img = new System.Windows.Controls.Image();
        img.SetBinding(System.Windows.Controls.Image.SourceProperty, new System.Windows.Data.Binding(nameof(ImageElementViewModel.ImageSource)));
        img.SetBinding(System.Windows.UIElement.OpacityProperty,     new System.Windows.Data.Binding(nameof(ImageElementViewModel.Opacity)));
        img.DataContext = vm;
        return img;
    }

    private static UIElement BuildShapeView(ShapeElementViewModel vm) => vm.ShapeType switch
    {
        Core.Models.ShapeType.Ellipse => BuildEllipse(vm),
        Core.Models.ShapeType.Line    => BuildLine(vm),
        _                             => BuildRectangle(vm)
    };

    private static System.Windows.Shapes.Rectangle BuildRectangle(ShapeElementViewModel vm)
    {
        var r = new System.Windows.Shapes.Rectangle();
        r.SetBinding(System.Windows.Shapes.Shape.FillProperty,             new System.Windows.Data.Binding(nameof(ShapeElementViewModel.FillBrush)));
        r.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,           new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeBrush)));
        r.SetBinding(System.Windows.Shapes.Shape.StrokeThicknessProperty,  new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeThickness)));
        r.SetBinding(System.Windows.Shapes.Rectangle.RadiusXProperty,      new System.Windows.Data.Binding(nameof(ShapeElementViewModel.CornerRadius)));
        r.SetBinding(System.Windows.Shapes.Rectangle.RadiusYProperty,      new System.Windows.Data.Binding(nameof(ShapeElementViewModel.CornerRadius)));
        r.DataContext = vm;
        return r;
    }

    private static System.Windows.Shapes.Ellipse BuildEllipse(ShapeElementViewModel vm)
    {
        var e = new System.Windows.Shapes.Ellipse();
        e.SetBinding(System.Windows.Shapes.Shape.FillProperty,            new System.Windows.Data.Binding(nameof(ShapeElementViewModel.FillBrush)));
        e.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,          new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeBrush)));
        e.SetBinding(System.Windows.Shapes.Shape.StrokeThicknessProperty, new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeThickness)));
        e.DataContext = vm;
        return e;
    }

    private static System.Windows.Shapes.Line BuildLine(ShapeElementViewModel vm)
    {
        var l = new System.Windows.Shapes.Line { X1 = 0, Y1 = 0 };
        l.SetBinding(System.Windows.Shapes.Line.X2Property,               new System.Windows.Data.Binding(nameof(ElementViewModelBase.Width)));
        l.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,          new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeBrush)));
        l.SetBinding(System.Windows.Shapes.Shape.StrokeThicknessProperty, new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeThickness)));
        l.DataContext = vm;
        return l;
    }
}
