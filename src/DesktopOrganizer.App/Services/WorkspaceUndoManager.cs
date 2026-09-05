using DesktopOrganizer.Core.Models;

namespace DesktopOrganizer.Services;

public sealed class WorkspaceUndoManager(int capacity = 30)
{
    private readonly Stack<WorkspaceUndoEntry> _entries = new();
    private readonly int _capacity = Math.Max(1, capacity);

    public bool CanUndo => _entries.Count > 0;

    public void Push(LayoutSnapshot snapshot) => PushEntry(new WorkspaceUndoEntry(snapshot.Clone(), []));

    public void PushFileMove(LayoutSnapshot snapshot, IReadOnlyList<FileMoveRecord> moves) =>
        PushEntry(new WorkspaceUndoEntry(snapshot.Clone(), moves.ToArray()));

    public WorkspaceUndoEntry? Undo() => _entries.TryPop(out var entry) ? entry : null;

    public void Clear() => _entries.Clear();

    private void PushEntry(WorkspaceUndoEntry entry)
    {
        _entries.Push(entry);
        if (_entries.Count <= _capacity)
        {
            return;
        }

        var retained = _entries.Take(_capacity).Reverse().ToArray();
        _entries.Clear();
        foreach (var item in retained)
        {
            _entries.Push(item);
        }
    }
}

public sealed record WorkspaceUndoEntry(
    LayoutSnapshot Snapshot,
    IReadOnlyList<FileMoveRecord> FileMoves);
