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
