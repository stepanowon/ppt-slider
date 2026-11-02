using System.Windows;
using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>
/// How every edit of the current slide runs: the text editor closes first, an undo snapshot is taken, the change is
/// made, the slide is redrawn. The commands use this instead of repeating it.
/// </summary>
sealed class SlideEdits(IEditorHost host, ShapeSelection selection, TextEditor text)
{
    /// <summary>Makes <paramref name="change"/> to the current slide with undo and redraw; false when no slide is open.</summary>
    public bool Apply(Action change)
    {
        text.Commit();
        if (host.Deck is not { } deck || host.CurrentSlide is not { } sp) return false;
        deck.History.Snapshot(sp);
        change();
        host.Changed();
        return true;
    }

    /// <summary>
    /// Runs <paramref name="f"/> on each selected shape it applies to (<paramref name="can"/>; default: shapes with a fill
    /// and outline) with where it is drawn; says to select one first when none applies.
    /// </summary>
    public void OnShapes(Action<OpenXmlElement, Rect> f, Func<OpenXmlElement, bool>? can = null)
    {
        text.Commit();
        var targets = selection.Hits.Where(h => (can ?? ShapeStyle.CanFormatShape)(h.Element)).ToList();
        if (host.Deck == null || targets.Count == 0) { host.Status(L.T("selectShapeFirst")); return; }
        Apply(() => { foreach (var h in targets) f(h.Element, h.Bounds); });
    }

    /// <summary>Runs <paramref name="f"/> on every selected shape with text (or table); says to select one first when there is none.</summary>
    public void OnText(Action<OpenXmlElement> f)
    {
        text.Commit();
        var targets = selection.Where(TextFormat.HasFormat).ToList();
        if (host.Deck == null || targets.Count == 0) { host.Status(L.T("selectTextFirst")); return; }
        Apply(() => { foreach (var t in targets) f(t); });
    }
}
