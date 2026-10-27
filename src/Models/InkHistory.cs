using System.Windows.Ink;

namespace PptSlider;

/// <summary>Undo for ink: remembers every change to a tracked stroke collection (a stroke drawn, erased, or all cleared).</summary>
public sealed class InkHistory
{
    readonly Dictionary<StrokeCollection, Stack<(StrokeCollection Added, StrokeCollection Removed)>> log = [];
    bool undoing;

    public void Track(StrokeCollection strokes)
    {
        if (log.ContainsKey(strokes)) return;
        var changes = log[strokes] = new();
        strokes.StrokesChanged += (_, e) => { if (!undoing && log.ContainsKey(strokes)) changes.Push((new(e.Added), new(e.Removed))); };
    }

    public bool CanUndo(StrokeCollection strokes) => log.TryGetValue(strokes, out var c) && c.Count > 0;

    /// <summary>Reverses the last change to <paramref name="strokes"/>; false when there is none.</summary>
    public bool Undo(StrokeCollection strokes)
    {
        if (!log.TryGetValue(strokes, out var changes) || !changes.TryPop(out var last)) return false;
        undoing = true;
        try
        {
            foreach (var s in last.Added) if (strokes.Contains(s)) strokes.Remove(s);
            foreach (var s in last.Removed) if (!strokes.Contains(s)) strokes.Add(s);
        }
        finally { undoing = false; }
        return true;
    }

    public void Clear() => log.Clear();
}
