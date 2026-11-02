using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>
/// The mouse on the slide with the selection tool: picking shapes (click, Shift/Ctrl+click, dragging a box), moving them,
/// the resize and rotate handles, double-click to edit text, drawing the shape picked in the Shapes gallery, and the
/// format painter's click. Points are in slide coordinates; the window's mouse handlers and the self-test come here.
/// </summary>
sealed class StageInteraction(IEditorHost host, ShapeSelection selection, SelectionAdorner adorner, TextEditor text, FormatPainterTool painter)
{
    DragKind drag;
    Point dragStart, dragEnd;
    Rect dragOrig, dragRect;
    Vector dragMove;
    double dragAngle;
    (int X, int Y) handle; // the resize handle being dragged
    /// <summary>The shape pressed on to move; a plain click (no drag) on one of several selected then selects just it.</summary>
    OpenXmlElement? clickedIn;

    /// <summary>A shape picked in the Shapes gallery, drawn by dragging on the slide next.</summary>
    public string? PendingShape { get; private set; }

    bool PendingLine => PendingShape is "line" or "arrow";

    /// <summary>The topmost shape at <paramref name="p"/> (a few pixels of slack, so thin lines can be picked).</summary>
    public ShapeHit? HitAt(Point p)
    {
        double t = 4 * host.Px;
        return host.Hits.LastOrDefault(h =>
        {
            var b = h.Bounds;
            b.Inflate(t, t);
            return b.Contains(SelectionAdorner.Turn(p, h.Bounds, -ShapeLayout.Rotation(h.Element)));
        });
    }

    void Show()
    {
        adorner.Preview = drag switch
        {
            DragKind.None => default,
            DragKind.Marquee => new(drag, dragRect),
            DragKind.Draw => new(drag, dragRect, Line: PendingLine ? (dragStart, dragEnd) : null),
            _ => new(drag, dragRect, dragMove, dragAngle),
        };
        adorner.Draw();
    }

    /// <summary>A press at <paramref name="p"/>; true when it was used (<paramref name="clicks"/>: 2 for a double-click).</summary>
    public bool Down(Point p, ModifierKeys mods, int clicks)
    {
        if (host.Deck == null) return false;
        text.Commit();
        double px = host.Px;
        bool add = (mods & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
        dragStart = dragEnd = p;
        dragMove = default;
        clickedIn = null;
        bool Begin(DragKind d) { drag = d; host.Surface.CaptureMouse(); Show(); return true; }

        if (PendingShape != null) { dragRect = new Rect(p, p); return Begin(DragKind.Draw); }
        if (painter.IsActive)
        {
            if (HitAt(p) is { } target) painter.PaintOn(target.Element); else painter.End();
            return true;
        }
        if (selection.Hits is [var cur])
        {
            double rot = ShapeLayout.Rotation(cur.Element);
            if (ShapeLayout.CanRotate(cur.Element) && (p - SelectionAdorner.Turn(SelectionAdorner.RotateHandle(cur.Bounds, px), cur.Bounds, rot)).Length < 8 * px)
            {
                dragAngle = rot;
                dragOrig = cur.Bounds;
                return Begin(DragKind.Rotate);
            }
            foreach (var hd in SelectionAdorner.Handles)
                if ((p - SelectionAdorner.Turn(SelectionAdorner.HandlePoint(cur.Bounds, hd), cur.Bounds, rot)).Length < 7 * px)
                {
                    handle = hd;
                    dragOrig = dragRect = cur.Bounds;
                    return Begin(DragKind.Resize);
                }
        }
        var hit = HitAt(p);
        if (hit == null)
        {
            if (!add) selection.Primary = null;
            selection.Cell = null;
            host.SyncFormatUi();
            dragRect = new Rect(p, p);
            return Begin(DragKind.Marquee); // drag a box around shapes to select them
        }
        if (add)
        {
            selection.Toggle(hit.Element);
            host.SyncFormatUi();
            adorner.Draw();
            return true;
        }
        if (!selection.Contains(hit.Element)) selection.Primary = hit.Element; // a click on one of several selected keeps them all (to move together)
        selection.Cell = Tables.IsTable(hit.Element) ? text.CellAt(hit.Element, p) : null;
        host.SyncFormatUi();
        if (clicks == 2)
        {
            if (TextContent.HasText(hit.Element)) { selection.Primary = hit.Element; text.Begin(p); return false; }
            if (selection.Cell is { } tc) { selection.Primary = hit.Element; text.EditCell(tc, p); return false; }
        }
        clickedIn = hit.Element;
        return Begin(DragKind.Move);
    }

    public void Move(Point p, ModifierKeys mods)
    {
        if (drag == DragKind.None) return;
        var d = p - dragStart;
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        switch (drag)
        {
            case DragKind.Move: dragMove = d; break;
            case DragKind.Marquee: dragRect = new Rect(dragStart, p); break;
            case DragKind.Draw:
                if (shift && !PendingLine) // Shift: a square / circle
                {
                    double side = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
                    p = dragStart + new Vector(d.X < 0 ? -side : side, d.Y < 0 ? -side : side);
                }
                dragEnd = p;
                dragRect = new Rect(dragStart, p);
                break;
            case DragKind.Resize: dragRect = Resized(d, shift ^ selection.Primary?.LocalName == "pic"); break; // pictures keep their shape unless Shift
            case DragKind.Rotate:
                var c = new Point(dragOrig.X + dragOrig.Width / 2, dragOrig.Y + dragOrig.Height / 2);
                double a = Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI + 90;
                if (shift) a = Math.Round(a / 15) * 15;
                dragAngle = (a % 360 + 360) % 360;
                break;
        }
        Show();
    }

    /// <summary>The shape's new box while a handle is dragged by <paramref name="d"/>; corners keep the proportions when asked.</summary>
    Rect Resized(Vector d, bool keepAspect)
    {
        var o = dragOrig;
        var el = selection.Primary;
        if (el != null && ShapeLayout.Rotation(el) is var rot && rot != 0)
            d = (Vector)new RotateTransform(-rot).Transform((Point)d); // into the shape's own axes
        double l = o.Left, t = o.Top, r = o.Right, b = o.Bottom;
        if (handle.X < 0) l += d.X; else if (handle.X > 0) r += d.X;
        if (handle.Y < 0) t += d.Y; else if (handle.Y > 0) b += d.Y;
        double min = el?.LocalName == "cxnSp" ? 0 : 8;
        if (r - l < min) { if (handle.X < 0) l = r - min; else r = l + min; }
        if (b - t < min) { if (handle.Y < 0) t = b - min; else b = t + min; }
        if (keepAspect && handle.X != 0 && handle.Y != 0 && o.Width > 0 && o.Height > 0)
        {
            double s = Math.Max((r - l) / o.Width, (b - t) / o.Height);
            if (handle.X < 0) l = r - o.Width * s; else r = l + o.Width * s;
            if (handle.Y < 0) t = b - o.Height * s; else b = t + o.Height * s;
        }
        return new Rect(l, t, r - l, b - t);
    }

    public void Up()
    {
        if (drag == DragKind.None) return;
        var was = drag;
        drag = DragKind.None;
        host.Surface.ReleaseMouseCapture();
        adorner.Preview = default;
        var deck = host.Deck!;
        var sp = host.CurrentSlide!;
        switch (was)
        {
            case DragKind.Marquee:
                if (dragRect.Width > 2 || dragRect.Height > 2)
                    foreach (var h in host.Hits.Where(h => dragRect.Contains(h.Bounds))) selection.Add(h.Element);
                host.SyncFormatUi();
                adorner.Draw();
                return;
            case DragKind.Draw:
                DrawPendingShape(dragStart, dragEnd);
                return;
            case DragKind.Move:
                if (dragMove.Length < 0.5)
                {
                    if (selection.Count > 1 && clickedIn != null) { selection.Primary = clickedIn; host.SyncFormatUi(); }
                    adorner.Draw();
                    return;
                }
                deck.History.Snapshot(sp);
                foreach (var h in selection.Hits) ShapeLayout.SetBounds(h.Element, Rect.Offset(h.Bounds, dragMove));
                break;
            case DragKind.Resize:
                if (selection.Primary is not { } el || (dragRect.TopLeft - dragOrig.TopLeft).Length < 0.5 && (dragRect.BottomRight - dragOrig.BottomRight).Length < 0.5) { adorner.Draw(); return; }
                deck.History.Snapshot(sp);
                ShapeLayout.SetBounds(el, dragRect);
                break;
            case DragKind.Rotate:
                if (selection.Primary is not { } turned || Math.Abs(dragAngle - ShapeLayout.Rotation(turned)) < 0.01) { adorner.Draw(); return; }
                deck.History.Snapshot(sp);
                ShapeLayout.SetRotation(turned, dragOrig, dragAngle);
                break;
        }
        dragMove = default;
        host.Changed();
    }

    /// <summary>The next drag on the slide draws <paramref name="kind"/> (Esc cancels).</summary>
    public void StartShape(string kind)
    {
        if (host.Deck == null) return;
        text.Commit();
        host.UseSelectTool();
        painter.End();
        PendingShape = kind;
        host.Surface.Cursor = Cursors.Cross;
        host.Status(L.T("drawShapeHint"));
    }

    /// <summary>Forgets the shape picked in the gallery.</summary>
    public void CancelShape()
    {
        PendingShape = null;
        host.Surface.Cursor = null;
    }

    /// <summary>Adds the shape picked in the gallery over the dragged box (a plain click: one inch at that point).</summary>
    public void DrawPendingShape(Point a, Point b)
    {
        if (host.Deck is not { } deck || PendingShape is not { } kind) return;
        CancelShape();
        bool line = kind is "line" or "arrow";
        var r = new Rect(a, b);
        if (r.Width < 3 && r.Height < 3) { r = new Rect(a.X, a.Y, 96, line ? 0 : 96); b = r.BottomRight; }
        deck.History.Snapshot(host.CurrentSlide!);
        selection.Primary = ShapeFactory.AddShape(host.CurrentSlide!, kind, r, line && b.X < a.X, line && b.Y < a.Y);
        host.Changed();
        host.SlideStatus();
    }
}
