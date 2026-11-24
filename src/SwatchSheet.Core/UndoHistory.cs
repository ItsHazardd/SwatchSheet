using System.Diagnostics.CodeAnalysis;

namespace SwatchSheet.Core;

/// <summary>Undo / redo by remembering whole snapshots (the sheet is small, so this is cheap).</summary>
public sealed class UndoHistory<T>
{
    private readonly List<T> _undo = new();
    private readonly List<T> _redo = new();
    private readonly int _max;

    public UndoHistory(int max = 200) => _max = Math.Max(1, max);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Call with the state from just BEFORE a change.</summary>
    public void Record(T before)
    {
        _undo.Add(before);
        if (_undo.Count > _max) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>What Undo would go back to, without doing it.</summary>
    public bool TryPeekUndo([MaybeNullWhen(false)] out T previous)
    {
        previous = _undo.Count > 0 ? _undo[^1] : default;
        return _undo.Count > 0;
    }

    /// <summary>What Redo would go forward to, without doing it.</summary>
    public bool TryPeekRedo([MaybeNullWhen(false)] out T next)
    {
        next = _redo.Count > 0 ? _redo[^1] : default;
        return _redo.Count > 0;
    }

    public bool TryUndo(T current, [MaybeNullWhen(false)] out T previous)
    {
        if (_undo.Count == 0) { previous = default; return false; }
        previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        return true;
    }

    public bool TryRedo(T current, [MaybeNullWhen(false)] out T next)
    {
        if (_redo.Count == 0) { next = default; return false; }
        next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        return true;
    }
}
