using DesktopOrganizer.Core.Models;

namespace DesktopOrganizer.Core.Services;

public sealed class LayoutHistory(int capacity = 30)
{
    private readonly Stack<LayoutSnapshot> _undoStack = new();
    private readonly int _capacity = Math.Max(1, capacity);

    public bool CanUndo => _undoStack.Count > 0;

    public void Push(LayoutSnapshot snapshot)
    {
        _undoStack.Push(snapshot.Clone());
        if (_undoStack.Count <= _capacity)
        {
            return;
        }

        var retained = _undoStack.Take(_capacity).Reverse().ToArray();
        _undoStack.Clear();
        foreach (var item in retained)
        {
            _undoStack.Push(item);
        }
    }

    public LayoutSnapshot? Undo()
    {
        return _undoStack.TryPop(out var snapshot) ? snapshot.Clone() : null;
    }

    public void Clear() => _undoStack.Clear();
}
