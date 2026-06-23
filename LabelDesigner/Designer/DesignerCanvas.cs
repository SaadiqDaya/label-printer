using LabelDesigner.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelDesigner.Designer;

public enum DesignerTool { Select, DrawLine }

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
    /// <summary>Snapshot of starting positions for every selected item so we can group-drag.</summary>
    private Dictionary<DesignerItem, Point>? _dragGroupOrigins;

    // ─── Smart-snap guide state (built at drag start, cleared on mouse up) ──
    private const double SnapThreshold = 3.0;   // design px (96-DPI); at the usual 4× zoom ≈ 12 screen px
    private List<double>? _snapTargetsX, _snapTargetsY;
    private System.Windows.Shapes.Line? _guideV, _guideH;

    /// <summary>All currently selected items (the primary is also a member).</summary>
    private readonly HashSet<DesignerItem> _selectedItems = new();

    // ─── Rubber-band state ───────────────────────────────────────────────
    private bool _isRubberBanding;
    private Point _rubberBandStart;
    private System.Windows.Shapes.Rectangle? _rubberBandVisual;

    // ─── Line-draw state ──────────────────────────────────────────────────
    private Point _lineStart;
    private ShapeElementViewModel? _drawingLineVm;
    private bool _isLineDrawing;

    public event EventHandler<ElementViewModelBase?>? SelectionChanged;

    /// <summary>Raised when an element is double-clicked (e.g. to open the table data editor).</summary>
    public event EventHandler<ElementViewModelBase>? ElementDoubleClicked;

    public DesignerTool CurrentTool { get; set; } = DesignerTool.Select;

    public DesignerItem? SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (_selectedItem == value) return;
            // NOTE: we no longer clear IsSelected on the old _selectedItem here — multi-selection
            // means the old item may still be in the selection set. ApplySelection() owns that flag.
            _selectedItem = value;
            SelectionChanged?.Invoke(this, _selectedItem?.ViewModel);
        }
    }

    /// <summary>Replaces the selection with a single item (or clears it when item == null).</summary>
    private void ReplaceSelection(DesignerItem? item)
    {
        foreach (var i in _selectedItems) { i.IsSelected = false; i.ViewModel.IsSelected = false; }
        _selectedItems.Clear();
        if (item != null)
        {
            _selectedItems.Add(item);
            item.IsSelected = true;
            item.ViewModel.IsSelected = true;
        }
        SelectedItem = item;
        PushSelectionToVm();
    }

    /// <summary>
    /// Selects an item plus every other member of its persistent group (clicking any member
    /// grabs the whole group). Falls back to single selection for ungrouped elements.
    /// </summary>
    private void SelectWithGroup(DesignerItem item)
    {
        var gid = item.ViewModel.GroupId;
        if (gid == null) { ReplaceSelection(item); return; }

        foreach (var i in _selectedItems) { i.IsSelected = false; i.ViewModel.IsSelected = false; }
        _selectedItems.Clear();
        foreach (var di in Children.OfType<DesignerItem>().Where(d => d.ViewModel.GroupId == gid))
        {
            _selectedItems.Add(di);
            di.IsSelected = true;
            di.ViewModel.IsSelected = true;
        }
        SelectedItem = item;
        PushSelectionToVm();
    }

    /// <summary>Adds the remaining members of any partially-selected group (rubber-band semantics).</summary>
    private void ExpandSelectionToGroups()
    {
        var gids = _selectedItems
            .Select(i => i.ViewModel.GroupId)
            .Where(g => g != null)
            .Select(g => g!.Value)
            .ToHashSet();
        if (gids.Count == 0) return;
        foreach (var di in Children.OfType<DesignerItem>()
                     .Where(d => d.ViewModel.GroupId.HasValue && gids.Contains(d.ViewModel.GroupId.Value)))
        {
            if (_selectedItems.Add(di))
            {
                di.IsSelected = true;
                di.ViewModel.IsSelected = true;
            }
        }
    }

    /// <summary>Toggles an item's presence in the multi-selection (Ctrl-click semantic).</summary>
    private void ToggleInSelection(DesignerItem item)
    {
        if (_selectedItems.Remove(item))
        {
            item.IsSelected = false;
            item.ViewModel.IsSelected = false;
            // Promote any remaining selected item to primary, or clear primary.
            SelectedItem = _selectedItems.FirstOrDefault();
        }
        else
        {
            _selectedItems.Add(item);
            item.IsSelected = true;
            item.ViewModel.IsSelected = true;
            SelectedItem = item;
        }
        PushSelectionToVm();
    }

    /// <summary>Pushes the current set into DesignerViewModel.SelectedElements so commands see it.</summary>
    private void PushSelectionToVm()
    {
        if (DataContext is not DesignerViewModel vm) return;
        vm.SelectedElements.Clear();
        foreach (var i in _selectedItems) vm.SelectedElements.Add(i.ViewModel);
        // Re-assert every member's IsSelected: setting SelectedItem above ran the VM's single-select
        // sync (SelectElement), which cleared the flag on all but the primary.
        foreach (var i in _selectedItems) i.ViewModel.IsSelected = true;
        CommandManager.InvalidateRequerySuggested();
    }

    public void AddElement(ElementViewModelBase viewModel)
    {
        var item = CreateItem(viewModel);
        Children.Add(item);
        ReplaceSelection(item);
    }

    public void ClearAll()
    {
        ReplaceSelection(null);
        // Detach all per-item handlers before clearing so old DesignerItems can be GC'd.
        foreach (var di in Children.OfType<DesignerItem>().ToList())
            DetachItemHandlers(di);
        Children.Clear();
    }

    /// <summary>Selects the canvas item whose ViewModel matches <paramref name="vm"/>.</summary>
    public void SelectByViewModel(ElementViewModelBase? vm)
    {
        if (vm == null) { ReplaceSelection(null); return; }
        var item = Children.OfType<DesignerItem>().FirstOrDefault(i => i.ViewModel == vm);
        ReplaceSelection(item);
    }

    public IEnumerable<ElementViewModelBase> GetAllViewModels() =>
        Children.OfType<DesignerItem>().Select(i => i.ViewModel);

    /// <summary>
    /// Directly stamps Panel.ZIndex on each DesignerItem from ViewModel.EffectiveZIndex.
    /// Called after layer reordering so Canvas re-renders even if the binding is stale.
    /// </summary>
    public void ApplyZIndices()
    {
        foreach (var item in Children.OfType<DesignerItem>())
            Panel.SetZIndex(item, item.ViewModel.EffectiveZIndex);
    }

    public void RemoveByViewModel(ElementViewModelBase viewModel)
    {
        var item = Children.OfType<DesignerItem>().FirstOrDefault(i => i.ViewModel == viewModel);
        if (item == null) return;
        if (_selectedItems.Remove(item))
        {
            item.IsSelected = false;
            item.ViewModel.IsSelected = false;
            if (SelectedItem == item) SelectedItem = _selectedItems.FirstOrDefault();
            PushSelectionToVm();
        }
        DetachItemHandlers(item);
        Children.Remove(item);
    }

    /// <summary>
    /// Removes every event handler this canvas attached to an item.
    /// Critical: every += in CreateItem must have a matching -= here, or the item
    /// (and its ViewModel) will be pinned in memory forever.
    /// </summary>
    private void DetachItemHandlers(DesignerItem item)
    {
        item.MouseDown        -= Item_MouseDown;
        item.MouseMove        -= Item_MouseMove;
        item.MouseUp          -= Item_MouseUp;
        item.ResizeCompleted  -= Item_ResizeCompleted;
        item.LineEndpointsChanged -= Item_LineEndpointsChanged;
    }

    private void Item_LineEndpointsChanged(object? sender, LineEndpointsChangedArgs args)
    {
        if (DataContext is DesignerViewModel designer)
            designer.UndoManager.Push(new LineEndpointsAction(
                args.Vm,
                args.OldX, args.OldY, args.OldW, args.OldH, args.OldReverseY,
                args.Vm.X, args.Vm.Y, args.Vm.Width, args.Vm.Height, args.Vm.LineReverseY));
    }

    private void Item_ResizeCompleted(object? sender, ResizeCompletedArgs args)
    {
        if (DataContext is DesignerViewModel designer)
            designer.UndoManager.Push(new ResizeAction(
                args.Vm,
                args.OldX, args.OldY, args.OldW, args.OldH,
                args.Vm.X, args.Vm.Y, args.Vm.Width, args.Vm.Height));
    }

    // ─── Canvas mouse events ───────────────────────────────────────────────
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        // ── Line drawing mode ──────────────────────────────────────────────
        // Accept the press wherever it lands — on bare canvas OR on top of an existing element
        // (Item_MouseDown defers to us while this tool is active). A real label is mostly covered
        // by elements, so requiring bare canvas made the tool look broken.
        if (CurrentTool == DesignerTool.DrawLine &&
            (e.Source == this || e.Source is DesignerItem) &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            _lineStart = e.GetPosition(this);
            _drawingLineVm = new ShapeElementViewModel
            {
                X = _lineStart.X,
                Y = _lineStart.Y,
                Width  = 2,
                Height = 2,
                ShapeType       = Core.Models.ShapeType.Line,
                StrokeColor     = "#000000",
                StrokeThickness = 2
            };

            // Adding through DesignerViewModel ensures it's tracked and fields/layers synced
            if (DataContext is DesignerViewModel designer)
                designer.AddElement(_drawingLineVm);

            _isLineDrawing = true;
            CaptureMouse();
            Focus();
            e.Handled = true;
            return;
        }

        if (e.Source == this)
        {
            // Empty-canvas click — start rubber-band selection. Ctrl preserves the existing
            // selection so users can extend it with a drag-box.
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (!ctrl) ReplaceSelection(null);
            BeginRubberBand(e.GetPosition(this));
            Focus();
            e.Handled = true;
        }
    }

    private void BeginRubberBand(Point start)
    {
        _isRubberBanding = true;
        _rubberBandStart = start;
        _rubberBandVisual = new System.Windows.Shapes.Rectangle
        {
            Stroke          = new SolidColorBrush(Color.FromRgb(30, 144, 255)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill            = new SolidColorBrush(Color.FromArgb(40, 30, 144, 255)),
            IsHitTestVisible = false,
            Width = 0, Height = 0
        };
        SetLeft(_rubberBandVisual, start.X);
        SetTop(_rubberBandVisual, start.Y);
        Panel.SetZIndex(_rubberBandVisual, int.MaxValue);
        Children.Add(_rubberBandVisual);
        CaptureMouse();
    }

    private void UpdateRubberBand(Point current)
    {
        if (_rubberBandVisual == null) return;
        double x = Math.Min(_rubberBandStart.X, current.X);
        double y = Math.Min(_rubberBandStart.Y, current.Y);
        double w = Math.Abs(current.X - _rubberBandStart.X);
        double h = Math.Abs(current.Y - _rubberBandStart.Y);
        SetLeft(_rubberBandVisual, x);
        SetTop(_rubberBandVisual, y);
        _rubberBandVisual.Width  = w;
        _rubberBandVisual.Height = h;
    }

    private void EndRubberBand()
    {
        if (!_isRubberBanding) return;
        _isRubberBanding = false;
        ReleaseMouseCapture();

        if (_rubberBandVisual == null) return;
        var rect = new Rect(GetLeft(_rubberBandVisual), GetTop(_rubberBandVisual),
                            _rubberBandVisual.Width, _rubberBandVisual.Height);
        Children.Remove(_rubberBandVisual);
        _rubberBandVisual = null;

        // Trivial clicks (no drag) shouldn't add anything — they already cleared selection.
        if (rect.Width < 2 && rect.Height < 2) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        // Ctrl extends; otherwise we already cleared above.
        foreach (var item in Children.OfType<DesignerItem>())
        {
            var bounds = new Rect(item.ViewModel.X, item.ViewModel.Y,
                                  item.ActualWidth, item.ActualHeight);
            if (rect.IntersectsWith(bounds))
            {
                _selectedItems.Add(item);
                item.IsSelected = true;
                item.ViewModel.IsSelected = true;
                SelectedItem = item; // last one becomes primary
            }
            else if (!ctrl)
            {
                // Selection was cleared at drag start; nothing to do.
            }
        }
        ExpandSelectionToGroups();   // a rubber-band that touches part of a group selects all of it
        PushSelectionToVm();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isRubberBanding)
        {
            UpdateRubberBand(e.GetPosition(this));
            return;
        }

        if (_isLineDrawing && _drawingLineVm != null)
        {
            var pos = e.GetPosition(this);
            double dx = pos.X - _lineStart.X;
            double dy = pos.Y - _lineStart.Y;

            // Shift-to-constrain: snap to horizontal, vertical, or 45° — whichever the drag is closest to.
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                double ax = Math.Abs(dx), ay = Math.Abs(dy);
                if (ax > ay * 2)        dy = 0;                          // mostly horizontal → flatten
                else if (ay > ax * 2)   dx = 0;                          // mostly vertical → flatten
                else                    { var m = Math.Max(ax, ay); dx = Math.Sign(dx) * m; dy = Math.Sign(dy) * m; } // 45°
            }

            // Bbox origin = top-left of the drag rectangle. LineReverseY flips the line so it
            // visually follows the drag direction in every quadrant.
            _drawingLineVm.X            = dx < 0 ? _lineStart.X + dx : _lineStart.X;
            _drawingLineVm.Y            = dy < 0 ? _lineStart.Y + dy : _lineStart.Y;
            _drawingLineVm.Width        = Math.Max(2, Math.Abs(dx));
            _drawingLineVm.Height       = Math.Max(1, Math.Abs(dy));
            // Anti-diagonal (dragging up-right or down-left) needs the line to start at (0,H) → (W,0).
            _drawingLineVm.LineReverseY = (dx > 0 && dy < 0) || (dx < 0 && dy > 0);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isRubberBanding)
        {
            EndRubberBand();
            e.Handled = true;
            return;
        }

        if (_isLineDrawing)
        {
            try
            {
                CurrentTool = DesignerTool.Select;
                Cursor = null;

                // Tell DesignerViewModel we're no longer in draw mode
                if (DataContext is DesignerViewModel designer)
                    designer.IsLineDrawMode = false;

                _drawingLineVm = null;
                e.Handled = true;
            }
            finally
            {
                // Always release capture, even if a handler above throws.
                _isLineDrawing = false;
                ReleaseMouseCapture();
            }
        }
    }

    // ─── Keyboard ─────────────────────────────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            // Cancel line drawing if active
            if (_isLineDrawing && _drawingLineVm != null)
            {
                _isLineDrawing = false;
                ReleaseMouseCapture();
                CurrentTool = DesignerTool.Select;
                Cursor = null;
                if (DataContext is DesignerViewModel d)
                {
                    d.IsLineDrawMode = false;
                    d.RemoveElement(_drawingLineVm);
                }
                _drawingLineVm = null;
                e.Handled = true;
                return;
            }
            ReplaceSelection(null);
            e.Handled = true;
            return;
        }

        if (_selectedItems.Count > 0 && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            double dy = e.Key == Key.Up   ? -step : e.Key == Key.Down  ? step : 0;

            // Clamp the group: the leftmost/topmost item can't go below 0, the rightmost/bottommost
            // can't exceed the canvas. Apply the same delta to all so the shape of the selection is
            // preserved. Locked members of the selection stay put.
            var items = _selectedItems.Where(i => !i.ViewModel.IsLocked).ToList();
            if (items.Count == 0) { e.Handled = true; return; }
            double minX = items.Min(i => i.ViewModel.X);
            double minY = items.Min(i => i.ViewModel.Y);
            if (DataContext is DesignerViewModel d)
            {
                double maxRight  = items.Max(i => i.ViewModel.X + i.ViewModel.Width);
                double maxBottom = items.Max(i => i.ViewModel.Y + i.ViewModel.Height);
                if (minX + dx < 0)                       dx = -minX;
                if (maxRight + dx > d.CanvasWidthPx)     dx = d.CanvasWidthPx  - maxRight;
                if (minY + dy < 0)                       dy = -minY;
                if (maxBottom + dy > d.CanvasHeightPx)   dy = d.CanvasHeightPx - maxBottom;
            }
            else
            {
                if (minX + dx < 0) dx = -minX;
                if (minY + dy < 0) dy = -minY;
            }

            foreach (var item in items)
            {
                var vm = item.ViewModel;
                var oldX = vm.X; var oldY = vm.Y;
                vm.X = oldX + dx;
                vm.Y = oldY + dy;
                if ((vm.X != oldX || vm.Y != oldY) && DataContext is DesignerViewModel designer)
                    designer.UndoManager.Push(new MoveAction(vm, oldX, oldY, vm.X, vm.Y));
            }

            e.Handled = true;
        }
    }

    // ─── Item creation ─────────────────────────────────────────────────────
    private DesignerItem CreateItem(ElementViewModelBase viewModel)
    {
        var item = new DesignerItem(viewModel);

        // Lines can be as thin as 1px in one axis, making them nearly impossible to click.
        // Give the DesignerItem a minimum hit area so users can grab horizontal or vertical lines.
        if (viewModel is ShapeElementViewModel { ShapeType: Core.Models.ShapeType.Line })
        {
            item.MinWidth  = 8;
            item.MinHeight = 8;
        }

        // Wrap the element's content in a background border so BackgroundColor is honoured
        UIElement inner = viewModel switch
        {
            TextElementViewModel    tv => BuildTextView(tv),
            BarcodeElementViewModel bv => BuildBarcodeView(bv),
            ImageElementViewModel   iv => BuildImageView(iv),
            ShapeElementViewModel   sv => BuildShapeView(sv),
            TableElementViewModel   tv => BuildTableView(tv),
            _                         => new TextBlock { Text = "?" }
        };

        var bg = new Border { DataContext = viewModel };
        bg.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding(nameof(ElementViewModelBase.BackgroundBrush)));
        bg.Child = inner as UIElement;
        item.Content = bg;

        item.MouseDown        += Item_MouseDown;
        item.MouseMove        += Item_MouseMove;
        item.MouseUp          += Item_MouseUp;
        item.ResizeCompleted  += Item_ResizeCompleted;
        item.LineEndpointsChanged += Item_LineEndpointsChanged;

        Focusable = true;
        return item;
    }

    // ─── Drag-to-move (single or group) ────────────────────────────────────
    private void Item_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // While the line tool is armed, presses must reach OnMouseDown to start drawing —
        // never select/drag the element under the cursor.
        if (CurrentTool == DesignerTool.DrawLine) return;

        var item = (DesignerItem)sender;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (ctrl)
        {
            ToggleInSelection(item);
            // Ctrl-click should not start a drag — the user is building a selection set.
            e.Handled = true;
            return;
        }

        // Double-click: select and hand off (the table data editor hooks this). No drag.
        if (e.ClickCount == 2)
        {
            if (!_selectedItems.Contains(item)) SelectWithGroup(item);
            ElementDoubleClicked?.Invoke(this, item.ViewModel);
            e.Handled = true;
            return;
        }

        // Plain click on an unselected item replaces selection (pulling in its whole persistent
        // group). Click on an already-selected item (in multi-mode) preserves the set so the user
        // can drag the whole group.
        if (!_selectedItems.Contains(item)) SelectWithGroup(item);
        else SelectedItem = item;

        // Locked elements can be selected (to inspect/unlock) but never dragged.
        if (item.ViewModel.IsLocked) { Focus(); e.Handled = true; return; }

        _dragStartMouse   = e.GetPosition(this);
        _dragStartElement = new Point(item.ViewModel.X, item.ViewModel.Y);
        // Snapshot origins for the movable part of the selection (locked members stay put).
        _dragGroupOrigins = _selectedItems.Where(i => !i.ViewModel.IsLocked)
                                          .ToDictionary(i => i, i => new Point(i.ViewModel.X, i.ViewModel.Y));
        BuildSnapTargets();
        _isDragging = true;
        item.CaptureMouse();
        Focus();
        e.Handled = true;
    }

    /// <summary>Collects the stationary edges (other elements + canvas) that the drag can snap to.</summary>
    private void BuildSnapTargets()
    {
        _snapTargetsX = null;
        _snapTargetsY = null;
        if (DataContext is not DesignerViewModel vm || !vm.SmartGuides) return;

        var xs = new List<double> { 0, vm.CanvasWidthPx, vm.CanvasWidthPx / 2 };
        var ys = new List<double> { 0, vm.CanvasHeightPx, vm.CanvasHeightPx / 2 };
        foreach (var di in Children.OfType<DesignerItem>()
                     .Where(d => !_selectedItems.Contains(d) && d.Visibility == Visibility.Visible))
        {
            var v = di.ViewModel;
            xs.Add(v.X); xs.Add(v.X + v.Width);  xs.Add(v.X + v.Width / 2);
            ys.Add(v.Y); ys.Add(v.Y + v.Height); ys.Add(v.Y + v.Height / 2);
        }
        _snapTargetsX = xs;
        _snapTargetsY = ys;
    }

    private void Item_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _selectedItem == null || _dragGroupOrigins == null) return;

        var pos = e.GetPosition(this);
        double dx = pos.X - _dragStartMouse.X;
        double dy = pos.Y - _dragStartMouse.Y;

        if (DataContext is DesignerViewModel vm && vm.SnapToGrid && vm.GridSize > 0)
        {
            var g = vm.GridSize;
            dx = Math.Round(dx / g) * g;
            dy = Math.Round(dy / g) * g;
        }

        ApplySmartSnap(ref dx, ref dy);

        // Move every selected item by the same delta, clamping so nothing goes negative.
        double minX = _dragGroupOrigins.Values.Min(p => p.X);
        double minY = _dragGroupOrigins.Values.Min(p => p.Y);
        if (minX + dx < 0) dx = -minX;
        if (minY + dy < 0) dy = -minY;

        foreach (var (di, origin) in _dragGroupOrigins)
        {
            di.ViewModel.X = origin.X + dx;
            di.ViewModel.Y = origin.Y + dy;
        }
    }

    /// <summary>Nudges the drag delta so the moving selection's bounding-box edges/centre align with
    /// nearby element or canvas edges, and shows pink guide lines at the matched coordinates.</summary>
    private void ApplySmartSnap(ref double dx, ref double dy)
    {
        if (_snapTargetsX == null || _snapTargetsY == null || _dragGroupOrigins == null || _dragGroupOrigins.Count == 0)
        {
            HideGuides();
            return;
        }

        // Bounding box of the selection at its ORIGIN, then shifted by the proposed delta.
        double oLeft   = _dragGroupOrigins.Min(p => p.Value.X);
        double oTop    = _dragGroupOrigins.Min(p => p.Value.Y);
        double oRight  = _dragGroupOrigins.Max(p => p.Value.X + p.Key.ViewModel.Width);
        double oBottom = _dragGroupOrigins.Max(p => p.Value.Y + p.Key.ViewModel.Height);

        var movingXs = new[] { oLeft + dx, oRight + dx, (oLeft + oRight) / 2 + dx };
        var movingYs = new[] { oTop + dy, oBottom + dy, (oTop + oBottom) / 2 + dy };

        var (cx, gx) = SnapSolver.Solve(movingXs, _snapTargetsX, SnapThreshold);
        var (cy, gy) = SnapSolver.Solve(movingYs, _snapTargetsY, SnapThreshold);
        dx += cx;
        dy += cy;

        UpdateGuide(ref _guideV, vertical: true,  gx);
        UpdateGuide(ref _guideH, vertical: false, gy);
    }

    private void UpdateGuide(ref System.Windows.Shapes.Line? guide, bool vertical, double? at)
    {
        if (at == null)
        {
            if (guide != null) { Children.Remove(guide); guide = null; }
            return;
        }
        if (guide == null)
        {
            guide = new System.Windows.Shapes.Line
            {
                Stroke           = new SolidColorBrush(Color.FromRgb(255, 64, 129)),
                StrokeThickness  = 0.5,
                StrokeDashArray  = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            Panel.SetZIndex(guide, int.MaxValue);
            Children.Add(guide);
        }
        if (vertical)
        {
            guide.X1 = guide.X2 = at.Value;
            guide.Y1 = 0; guide.Y2 = ActualHeight;
        }
        else
        {
            guide.Y1 = guide.Y2 = at.Value;
            guide.X1 = 0; guide.X2 = ActualWidth;
        }
    }

    private void HideGuides()
    {
        if (_guideV != null) { Children.Remove(_guideV); _guideV = null; }
        if (_guideH != null) { Children.Remove(_guideH); _guideH = null; }
    }

    private void Item_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging || _selectedItem == null) return;
        try
        {
            // Push one undo action per moved item so undo restores the whole group.
            if (_dragGroupOrigins != null && DataContext is DesignerViewModel designer)
            {
                foreach (var (di, origin) in _dragGroupOrigins)
                {
                    var vm = di.ViewModel;
                    if (vm.X != origin.X || vm.Y != origin.Y)
                        designer.UndoManager.Push(new MoveAction(vm, origin.X, origin.Y, vm.X, vm.Y));
                }
            }
        }
        finally
        {
            ((DesignerItem)sender).ReleaseMouseCapture();
            _isDragging = false;
            _dragGroupOrigins = null;
            _snapTargetsX = null;
            _snapTargetsY = null;
            HideGuides();
        }
    }

    // ─── View builders ──────────────────────────────────────────────────────
    private static UIElement BuildTextView(TextElementViewModel vm)
    {
        static System.Windows.Controls.TextBlock MakeTextBlock(TextElementViewModel vm, string fontSizePath)
        {
            var tb = new System.Windows.Controls.TextBlock();
            tb.SetBinding(System.Windows.Controls.TextBlock.TextProperty,          new System.Windows.Data.Binding(nameof(TextElementViewModel.PreviewText)));
            tb.SetBinding(System.Windows.Controls.TextBlock.FontFamilyProperty,    new System.Windows.Data.Binding(nameof(TextElementViewModel.FontFamily)) { Converter = new Converters.StringToFontFamilyConverter() });
            tb.SetBinding(System.Windows.Controls.TextBlock.FontSizeProperty,      new System.Windows.Data.Binding(fontSizePath));
            tb.SetBinding(System.Windows.Controls.TextBlock.FontWeightProperty,    new System.Windows.Data.Binding(nameof(TextElementViewModel.FontWeightValue)));
            tb.SetBinding(System.Windows.Controls.TextBlock.FontStyleProperty,     new System.Windows.Data.Binding(nameof(TextElementViewModel.FontStyleValue)));
            tb.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty,    new System.Windows.Data.Binding(nameof(TextElementViewModel.ForegroundBrush)));
            tb.SetBinding(System.Windows.Controls.TextBlock.TextAlignmentProperty, new System.Windows.Data.Binding(nameof(TextElementViewModel.TextAlignmentValue)));
            tb.SetBinding(System.Windows.Controls.TextBlock.TextWrappingProperty,  new System.Windows.Data.Binding(nameof(TextElementViewModel.TextWrappingValue)));
            tb.DataContext = vm;
            return tb;
        }

        // TWO visuals, exactly one visible (see TextElementViewModel.FitViewboxVisibility):
        // a plain TextBlock constrained by the element box — wraps multi-line text exactly like the
        // printed output — and a Viewbox-scaled copy for single-line FitToBox. The old single-Viewbox
        // approach could never wrap (a Viewbox measures its child with infinite width).
        // Plain block uses EffectiveFontSize so multi-line FitToBox re-fits live (like print);
        // the Viewbox copy scales geometrically, so the declared FontSize is fine there.
        var plain = MakeTextBlock(vm, nameof(TextElementViewModel.EffectiveFontSize));
        plain.SetBinding(UIElement.VisibilityProperty,
            new System.Windows.Data.Binding(nameof(TextElementViewModel.PlainTextVisibility)));

        var vb = new System.Windows.Controls.Viewbox { Stretch = Stretch.Uniform, DataContext = vm };
        vb.Child = MakeTextBlock(vm, nameof(TextElementViewModel.FontSize));
        vb.SetBinding(UIElement.VisibilityProperty,
            new System.Windows.Data.Binding(nameof(TextElementViewModel.FitViewboxVisibility)));

        var host = new Grid { DataContext = vm };
        host.Children.Add(plain);
        host.Children.Add(vb);
        return host;
    }

    private static System.Windows.Controls.Image BuildBarcodeView(BarcodeElementViewModel vm)
    {
        var img = new System.Windows.Controls.Image { Stretch = System.Windows.Media.Stretch.Fill };
        // Use high-quality downscaling so the barcode looks crisp at all zoom levels
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        img.SetBinding(System.Windows.Controls.Image.SourceProperty,
            new System.Windows.Data.Binding(nameof(BarcodeElementViewModel.BarcodeImage)));
        img.DataContext = vm;
        return img;
    }

    private static System.Windows.Controls.Image BuildImageView(ImageElementViewModel vm)
    {
        var img = new System.Windows.Controls.Image();
        img.SetBinding(System.Windows.Controls.Image.SourceProperty,  new System.Windows.Data.Binding(nameof(ImageElementViewModel.ImageSource)));
        img.SetBinding(System.Windows.UIElement.OpacityProperty,       new System.Windows.Data.Binding(nameof(ImageElementViewModel.Opacity)));
        img.DataContext = vm;
        return img;
    }

    private static UIElement BuildShapeView(ShapeElementViewModel vm) => vm.ShapeType switch
    {
        Core.Models.ShapeType.Ellipse  => BuildEllipse(vm),
        Core.Models.ShapeType.Line     => BuildLine(vm),
        Core.Models.ShapeType.Triangle => BuildPolygonShape(vm, new[] { "50,0", "100,100", "0,100" }),
        Core.Models.ShapeType.Arrow    => BuildPolygonShape(vm, new[] { "0,20", "60,20", "60,0", "100,50", "60,100", "60,80", "0,80" }),
        Core.Models.ShapeType.Diamond  => BuildPolygonShape(vm, new[] { "50,0", "100,50", "50,100", "0,50" }),
        _                              => BuildRectangle(vm)
    };

    private static System.Windows.Shapes.Rectangle BuildRectangle(ShapeElementViewModel vm)
    {
        var r = new System.Windows.Shapes.Rectangle();
        r.SetBinding(System.Windows.Shapes.Shape.FillProperty,            new System.Windows.Data.Binding(nameof(ShapeElementViewModel.FillBrush)));
        r.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,          new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeBrush)));
        r.SetBinding(System.Windows.Shapes.Shape.StrokeThicknessProperty, new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeThickness)));
        r.SetBinding(System.Windows.Shapes.Rectangle.RadiusXProperty,     new System.Windows.Data.Binding(nameof(ShapeElementViewModel.CornerRadius)));
        r.SetBinding(System.Windows.Shapes.Rectangle.RadiusYProperty,     new System.Windows.Data.Binding(nameof(ShapeElementViewModel.CornerRadius)));
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
        // Y1/Y2 swap based on LineReverseY so the line can go top-left↘bottom-right or
        // bottom-left↗top-right depending on the user's drag direction.
        var l = new System.Windows.Shapes.Line { X1 = 0 };
        l.SetBinding(System.Windows.Shapes.Line.X2Property,               new System.Windows.Data.Binding(nameof(ElementViewModelBase.Width)));
        l.SetBinding(System.Windows.Shapes.Line.Y1Property,               new System.Windows.Data.Binding(nameof(ShapeElementViewModel.LineY1)));
        l.SetBinding(System.Windows.Shapes.Line.Y2Property,               new System.Windows.Data.Binding(nameof(ShapeElementViewModel.LineY2)));
        l.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,          new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeBrush)));
        l.SetBinding(System.Windows.Shapes.Shape.StrokeThicknessProperty, new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeThickness)));
        l.DataContext = vm;
        return l;
    }

    private static UIElement BuildTableView(TableElementViewModel vm)
    {
        var border = new Border { DataContext = vm, BorderThickness = new Thickness(1), ClipToBounds = true };
        border.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding(nameof(TableElementViewModel.BorderBrush)));

        var outerGrid = new Grid();
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Header row ─────────────────────────────────────────────────────
        var headerBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding(nameof(TableElementViewModel.HeaderBrush)));
        headerBorder.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding(nameof(TableElementViewModel.BorderBrush)));

        // Each column header cell
        var headerCellFactory = new FrameworkElementFactory(typeof(Border));
        headerCellFactory.SetBinding(Border.WidthProperty,
            new System.Windows.Data.Binding(nameof(TableColumnViewModel.Width)));
        headerCellFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 0));
        headerCellFactory.SetValue(Border.PaddingProperty, new Thickness(3, 2, 3, 2));
        var headerTbFactory = new FrameworkElementFactory(typeof(TextBlock));
        headerTbFactory.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(TableColumnViewModel.Header)));
        headerTbFactory.SetValue(TextBlock.FontSizeProperty, 9.0);
        headerTbFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        headerCellFactory.AppendChild(headerTbFactory);
        var headerItemTemplate = new DataTemplate { VisualTree = headerCellFactory };

        var headerPanel = new FrameworkElementFactory(typeof(StackPanel));
        headerPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var headerIc = new ItemsControl { ItemTemplate = headerItemTemplate, DataContext = vm };
        headerIc.ItemsPanel = new ItemsPanelTemplate(headerPanel);
        headerIc.SetBinding(ItemsControl.ItemsSourceProperty,
            new System.Windows.Data.Binding(nameof(TableElementViewModel.Columns)));
        headerBorder.Child = headerIc;
        Grid.SetRow(headerBorder, 0);
        outerGrid.Children.Add(headerBorder);

        // ── Rows area: each row → horizontal strip of cell TextBlocks ────────
        // Cell template: TextBlock per cell in a row
        var cellTextFef = new FrameworkElementFactory(typeof(TextBlock));
        cellTextFef.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(TableCellViewModel.Value)));
        cellTextFef.SetValue(TextBlock.FontSizeProperty, 8.0);
        cellTextFef.SetValue(TextBlock.MinWidthProperty, 30.0);
        cellTextFef.SetValue(TextBlock.PaddingProperty, new Thickness(2, 1, 2, 1));
        var cellBorderFef = new FrameworkElementFactory(typeof(Border));
        cellBorderFef.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 0));
        cellBorderFef.AppendChild(cellTextFef);
        var cellTemplate = new DataTemplate { VisualTree = cellBorderFef };

        var cellPanelFef = new FrameworkElementFactory(typeof(StackPanel));
        cellPanelFef.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        // Row template: ItemsControl over Cells
        var rowCellsIcFef = new FrameworkElementFactory(typeof(ItemsControl));
        rowCellsIcFef.SetValue(ItemsControl.ItemTemplateProperty, cellTemplate);
        rowCellsIcFef.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(cellPanelFef));
        rowCellsIcFef.SetBinding(ItemsControl.ItemsSourceProperty,
            new System.Windows.Data.Binding(nameof(TableRowViewModel.Cells)));

        var rowBorderFef = new FrameworkElementFactory(typeof(Border));
        rowBorderFef.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        rowBorderFef.AppendChild(rowCellsIcFef);
        var rowTemplate = new DataTemplate { VisualTree = rowBorderFef };

        var rowsPanelFef = new FrameworkElementFactory(typeof(StackPanel));
        rowsPanelFef.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        var rowsIc = new ItemsControl { ItemTemplate = rowTemplate, DataContext = vm };
        rowsIc.ItemsPanel = new ItemsPanelTemplate(rowsPanelFef);
        rowsIc.SetBinding(ItemsControl.ItemsSourceProperty,
            new System.Windows.Data.Binding(nameof(TableElementViewModel.Rows)));

        var dataBorder = new Border();
        dataBorder.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding(nameof(TableElementViewModel.CellBrush)));
        dataBorder.Child = rowsIc;
        Grid.SetRow(dataBorder, 1);
        outerGrid.Children.Add(dataBorder);

        border.Child = outerGrid;

        // Scale the table to FILL the element box (so it resizes with the box instead of being
        // clipped) — same Viewbox-Fill the print path uses, so the canvas matches the output.
        return new System.Windows.Controls.Viewbox { Stretch = Stretch.Fill, Child = border };
    }

    private static System.Windows.Controls.Viewbox BuildPolygonShape(ShapeElementViewModel vm, string[] points)
    {
        var pts = new System.Windows.Media.PointCollection(
            points.Select(p => {
                var xy = p.Split(',');
                return new System.Windows.Point(double.Parse(xy[0]), double.Parse(xy[1]));
            }));
        var poly = new System.Windows.Shapes.Polygon { Points = pts };
        poly.SetBinding(System.Windows.Shapes.Shape.FillProperty,            new System.Windows.Data.Binding(nameof(ShapeElementViewModel.FillBrush)));
        poly.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,          new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeBrush)));
        poly.SetBinding(System.Windows.Shapes.Shape.StrokeThicknessProperty, new System.Windows.Data.Binding(nameof(ShapeElementViewModel.StrokeThickness)));
        poly.DataContext = vm;
        var vb = new System.Windows.Controls.Viewbox { Stretch = System.Windows.Media.Stretch.Fill };
        vb.Child = poly;
        return vb;
    }
}
