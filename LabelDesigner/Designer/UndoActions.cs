using LabelDesigner.Services;
using LabelDesigner.ViewModels;

namespace LabelDesigner.Designer;

/// <summary>Passed when a resize drag completes, carrying before-state for undo.</summary>
public class ResizeCompletedArgs(ElementViewModelBase vm,
    double oldX, double oldY, double oldW, double oldH) : EventArgs
{
    public ElementViewModelBase Vm { get; } = vm;
    public double OldX { get; } = oldX;
    public double OldY { get; } = oldY;
    public double OldW { get; } = oldW;
    public double OldH { get; } = oldH;
}

internal sealed class MoveAction(ElementViewModelBase vm,
    double oldX, double oldY, double newX, double newY) : IUndoAction
{
    public void Undo() { vm.X = oldX; vm.Y = oldY; }
    public void Redo() { vm.X = newX; vm.Y = newY; }
}

internal sealed class ResizeAction(ElementViewModelBase vm,
    double ox, double oy, double ow, double oh,
    double nx, double ny, double nw, double nh) : IUndoAction
{
    public void Undo() { vm.X = ox; vm.Y = oy; vm.Width = ow; vm.Height = oh; }
    public void Redo() { vm.X = nx; vm.Y = ny; vm.Width = nw; vm.Height = nh; }
}

/// <summary>Groups several actions (e.g. an align/distribute over many elements) into ONE undo step.</summary>
internal sealed class CompositeAction(IReadOnlyList<IUndoAction> actions) : IUndoAction
{
    public void Undo() { for (int i = actions.Count - 1; i >= 0; i--) actions[i].Undo(); }
    public void Redo() { foreach (var a in actions) a.Redo(); }
}

/// <summary>Undo/redo for a line-endpoint drag — restores the bounding box AND the diagonal
/// direction flag (a plain ResizeAction can't bring LineReverseY back).</summary>
internal sealed class LineEndpointsAction(ShapeElementViewModel vm,
    double ox, double oy, double ow, double oh, bool orev,
    double nx, double ny, double nw, double nh, bool nrev) : IUndoAction
{
    public void Undo() { vm.X = ox; vm.Y = oy; vm.Width = ow; vm.Height = oh; vm.LineReverseY = orev; }
    public void Redo() { vm.X = nx; vm.Y = ny; vm.Width = nw; vm.Height = nh; vm.LineReverseY = nrev; }
}

/// <summary>Undo/redo for Group (newId = the new group) and Ungroup (newId = null).</summary>
internal sealed class GroupAction(IReadOnlyDictionary<ElementViewModelBase, Guid?> oldIds, Guid? newId) : IUndoAction
{
    public void Undo() { foreach (var (vm, g) in oldIds) vm.GroupId = g; }
    public void Redo() { foreach (var vm in oldIds.Keys) vm.GroupId = newId; }
}

internal sealed class AddElementAction(ElementViewModelBase vm, DesignerViewModel designer) : IUndoAction
{
    public void Undo() => designer.RemoveElement(vm);
    public void Redo() => designer.AddElement(vm, recordUndo: false);
}

internal sealed class RemoveElementAction(ElementViewModelBase vm, DesignerViewModel designer) : IUndoAction
{
    public void Undo() => designer.AddElement(vm, recordUndo: false);
    public void Redo() => designer.RemoveElement(vm);
}
