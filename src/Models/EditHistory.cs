using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace PptSlider;

/// <summary>Undo / redo of slide edits: a copy of a slide's XML taken before each change.</summary>
public sealed class EditHistory
{
    readonly Stack<(SlidePart Part, string Xml)> undo = new();
    readonly Stack<(SlidePart Part, string Xml)> redo = new();
    readonly Func<SlidePart, bool> exists;

    /// <summary>A snapshot was taken or a slide put back: the document changed.</summary>
    public event Action? Changed;

    /// <param name="exists">Whether a slide is still in the deck (a deleted slide's snapshots are skipped).</param>
    public EditHistory(Func<SlidePart, bool> exists) => this.exists = exists;

    /// <summary>Call before mutating a slide.</summary>
    public void Snapshot(SlidePart sp)
    {
        undo.Push((sp, sp.Slide!.OuterXml));
        redo.Clear(); // a new edit: what was undone can no longer be redone
        Changed?.Invoke();
    }

    /// <summary>Puts back the slide as it was before the last edit; returns it (null: nothing to undo).</summary>
    public SlidePart? Undo() => Restore(undo, redo);

    /// <summary>Puts back the last edit <see cref="Undo"/> took back (Ctrl+Y).</summary>
    public SlidePart? Redo() => Restore(redo, undo);

    SlidePart? Restore(Stack<(SlidePart Part, string Xml)> from, Stack<(SlidePart Part, string Xml)> to)
    {
        while (from.TryPop(out var u))
        {
            if (!exists(u.Part)) continue; // slide was deleted since
            to.Push((u.Part, u.Part.Slide!.OuterXml));
            u.Part.Slide = new P.Slide(u.Xml);
            Changed?.Invoke();
            return u.Part;
        }
        return null;
    }
}
