using System.Windows.Input;

namespace LabelDesigner.Services;

public interface IUndoAction
{
    void Undo();
    void Redo();
}

/// <summary>
/// Bounded undo/redo stack.
/// Push an action after every user-initiated change.
/// Undo() reverses the most recent action; Redo() re-applies it.
/// </summary>
public class UndoRedoManager
{
    /// <summary>Maximum actions held per stack. Older actions are discarded silently.</summary>
    public const int MaxStackSize = 200;

    // LinkedList lets us trim from the bottom (oldest) in O(1) when the cap is hit.
    private readonly LinkedList<IUndoAction> _undo = new();
    private readonly LinkedList<IUndoAction> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Raised whenever CanUndo or CanRedo changes.</summary>
    public event EventHandler? StateChanged;

    public void Push(IUndoAction action)
    {
        _undo.AddFirst(action);
        if (_undo.Count > MaxStackSize) _undo.RemoveLast();
        _redo.Clear();
        OnStateChanged();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var a = _undo.First!.Value;
        _undo.RemoveFirst();
        a.Undo();
        _redo.AddFirst(a);
        if (_redo.Count > MaxStackSize) _redo.RemoveLast();
        OnStateChanged();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var a = _redo.First!.Value;
        _redo.RemoveFirst();
        a.Redo();
        _undo.AddFirst(a);
        if (_undo.Count > MaxStackSize) _undo.RemoveLast();
        OnStateChanged();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        OnStateChanged();
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        // Nudge WPF to re-evaluate any RelayCommand.CanExecute that depends on us.
        CommandManager.InvalidateRequerySuggested();
    }
}
