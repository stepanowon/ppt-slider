using System.Windows.Ink;

namespace PptSlider;

/// <summary>
/// Undo for ink: remembers the changes to each tracked stroke collection (a stroke drawn, erased, or all cleared), the last
/// <see cref="Limit"/> of each (a cleared slide keeps its strokes here, so undo can bring them back).
/// </summary>
public sealed class InkHistory
{
    public const int Limit = 200;
    readonly Dictionary<StrokeCollection, (BoundedStack<(StrokeCollection Added, StrokeCollection Removed)> Changes, StrokeCollectionChangedEventHandler Handler)> log = [];
    bool undoing;

    public void Track(StrokeCollection strokes)
    {
        if (log.ContainsKey(strokes)) return;
        var changes = new BoundedStack<(StrokeCollection, StrokeCollection)>(Limit);
        StrokeCollectionChangedEventHandler handler = (_, e) => { if (!undoing) changes.Push((new(e.Added), new(e.Removed))); };
        strokes.StrokesChanged += handler;
        log[strokes] = (changes, handler);
    }

    /// <summary>Forgets <paramref name="strokes"/> and its changes (a deleted slide, a blank screen that is gone).</summary>
    public void Untrack(StrokeCollection strokes)
    {
        if (!log.Remove(strokes, out var entry)) return;
        strokes.StrokesChanged -= entry.Handler;
    }

    public bool CanUndo(StrokeCollection strokes) => log.TryGetValue(strokes, out var c) && c.Changes.Count > 0;

    /// <summary>Reverses the last change to <paramref name="strokes"/>; false when there is none.</summary>
    public bool Undo(StrokeCollection strokes)
    {
        if (!log.TryGetValue(strokes, out var entry) || !entry.Changes.TryPop(out var last)) return false;
        undoing = true;
        try
        {
            foreach (var s in last.Added) if (strokes.Contains(s)) strokes.Remove(s);
            foreach (var s in last.Removed) if (!strokes.Contains(s)) strokes.Add(s);
        }
        finally { undoing = false; }
        return true;
    }

    public void Clear()
    {
        foreach (var strokes in log.Keys.ToList()) Untrack(strokes);
    }
}
