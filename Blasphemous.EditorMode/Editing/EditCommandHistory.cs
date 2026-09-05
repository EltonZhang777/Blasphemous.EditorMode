using Blasphemous.EditorMode.Diagnostics;
using System.Collections.Generic;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Reversible domain command used by the editing session.</summary>
internal interface IReversibleEditCommand
{
    string Name { get; }
    bool Undo(ApplyReport report);
    bool Redo(ApplyReport report);
}

/// <summary>Undo/redo stack with redo invalidation after a new command.</summary>
internal sealed class EditCommandHistory
{
    private readonly Stack<IReversibleEditCommand> _undo = new Stack<IReversibleEditCommand>();
    private readonly Stack<IReversibleEditCommand> _redo = new Stack<IReversibleEditCommand>();

    internal int UndoCount { get { return _undo.Count; } }
    internal int RedoCount { get { return _redo.Count; } }
    internal bool CanUndo { get { return _undo.Count > 0; } }
    internal bool CanRedo { get { return _redo.Count > 0; } }

    internal void Record(IReversibleEditCommand command)
    {
        _undo.Push(command);
        _redo.Clear();
    }

    internal bool Undo(ApplyReport report)
    {
        if (_undo.Count == 0)
        {
            report.Error("UNDO_EMPTY", "$", "There is no edit command to undo.", false);
            return false;
        }

        IReversibleEditCommand command = _undo.Pop();
        if (!command.Undo(report))
        {
            _undo.Push(command);
            return false;
        }

        _redo.Push(command);
        return true;
    }

    internal bool Redo(ApplyReport report)
    {
        if (_redo.Count == 0)
        {
            report.Error("REDO_EMPTY", "$", "There is no edit command to redo.", false);
            return false;
        }

        IReversibleEditCommand command = _redo.Pop();
        if (!command.Redo(report))
        {
            _redo.Push(command);
            return false;
        }

        _undo.Push(command);
        return true;
    }

    internal void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
