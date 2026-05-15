namespace LabelDesigner.Services;

public interface IUndoAction
{
    void Undo();
    void Redo();
}

/// <summary>
/// Simple undo/redo stack.
/// Push an action after every user-initiated change.
/// Undo() reverses the most recent action; Redo() re-applies it.
/// </summary>
public class UndoRedoManager
{
    private readonly Stack<IUndoAction> _undoStack = new();
    private readonly Stack<IUndoAction> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Raised whenever CanUndo or CanRedo changes.</summary>
    public event EventHandler? StateChanged;

    public void Push(IUndoAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var a = _undoStack.Pop();
        a.Undo();
        _redoStack.Push(a);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var a = _redoStack.Pop();
        a.Redo();
        _undoStack.Push(a);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
