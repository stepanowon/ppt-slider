using System.Windows;
using System.Windows.Media;
using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>Commands on the selected shapes: select, delete, nudge, drawing order, align, fill / outline, rotate / flip.</summary>
sealed class ShapeCommands(IEditorHost host, ShapeSelection selection, SelectionAdorner adorner, TextEditor text, SlideEdits edits)
{
    /// <summary>Selects these shapes, as clicking them with Shift does.</summary>
    public void Select(params OpenXmlElement[] els)
    {
        text.Commit();
        selection.Set(els);
        host.SyncFormatUi();
        adorner.Draw();
    }

    /// <summary>Ctrl+A: every shape on the slide.</summary>
    public void SelectAll()
    {
        if (host.Deck == null) return;
        text.Commit();
        host.UseSelectTool();
        selection.Set(host.Hits.Select(h => h.Element));
        host.SyncFormatUi();
        adorner.Draw();
    }

    public void Delete()
    {
        if (host.Deck == null || selection.Count == 0) return;
        edits.Apply(() =>
        {
            foreach (var el in selection) el.Remove();
            selection.Reset();
        });
    }

    /// <summary>Arrow keys move the selected shapes (Ctrl: one pixel).</summary>
    public void Nudge(double dx, double dy)
    {
        if (host.Deck == null) return;
        var shown = selection.Hits;
        edits.Apply(() => { foreach (var h in shown) ShapeLayout.SetBounds(h.Element, Rect.Offset(h.Bounds, new Vector(dx, dy))); });
    }

    /// <summary>"front", "back", "forward" or "backward".</summary>
    public void Arrange(string how)
    {
        if (host.Deck == null || selection.Count == 0) { host.Status(L.T("selectShapeFirst")); return; }
        var els = selection.ToList();
        edits.Apply(() => SlideTree.Arrange(els, how));
    }

    /// <summary>Lines up the selected shapes: with each other when there are several, else on the slide.</summary>
    public void Align(string how)
    {
        var shown = selection.Hits;
        if (host.Deck is not { } deck || shown.Count == 0) { host.Status(L.T("selectShapeFirst")); return; }
        var f = shown.Count > 1 ? shown.Select(h => h.Bounds).Aggregate(Rect.Union) : new Rect(0, 0, deck.Renderer.Width, deck.Renderer.Height);
        edits.OnShapes((el, r) =>
        {
            double x = how switch { "left" => f.Left, "center" => f.Left + (f.Width - r.Width) / 2, "right" => f.Right - r.Width, _ => r.X };
            double y = how switch { "top" => f.Top, "middle" => f.Top + (f.Height - r.Height) / 2, "bottom" => f.Bottom - r.Height, _ => r.Y };
            ShapeLayout.SetBounds(el, new Rect(x, y, r.Width, r.Height));
        }, _ => true);
    }

    public void SetFill(Color? c) => edits.OnShapes((el, _) => ShapeStyle.SetFill(el, c), el => ShapeStyle.CanFormatShape(el) && el.LocalName != "cxnSp");
    public void SetOutline(Color? c) => edits.OnShapes((el, _) => ShapeStyle.SetLineColor(el, c));
    public void SetOutlineWidth(double pt) => edits.OnShapes((el, _) => ShapeStyle.SetLineWidth(el, pt));
    public void SetDash(string dash) => edits.OnShapes((el, _) => ShapeStyle.SetDash(el, dash));
    public void Rotate(double by) => edits.OnShapes((el, r) => ShapeLayout.SetRotation(el, r, ShapeLayout.Rotation(el) + by), ShapeLayout.CanRotate);
    public void Flip(bool horizontal) => edits.OnShapes((el, r) => ShapeLayout.Flip(el, r, horizontal), ShapeLayout.CanRotate);
}
