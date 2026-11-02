using System.Windows.Input;
using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>
/// Format Painter (the brush: the next shape clicked takes the selected shape's look) and Ctrl+Shift+C / Ctrl+Shift+V.
/// </summary>
sealed class FormatPainterTool(IEditorHost host, ShapeSelection selection, TextEditor text, SlideEdits edits)
{
    /// <summary>The format the brush carries until the next click on a shape.</summary>
    ShapeFormat? brush;
    /// <summary>Ctrl+Shift+C: the format Ctrl+Shift+V gives the selected shapes.</summary>
    ShapeFormat? copied;

    public bool IsActive => brush != null;

    /// <summary>Picks up the selected shape's format; the next click on a shape gives it that format.</summary>
    public void Start()
    {
        text.Commit();
        if (selection.Primary is not { } src) { host.Status(L.T("selectShapeFirst")); return; }
        brush = FormatPainter.Take(src);
        host.Surface.Cursor = Cursors.Hand;
        host.Status(L.T("painterHint"));
    }

    /// <summary>Gives <paramref name="target"/> the format picked up, once; it becomes the selection.</summary>
    public void PaintOn(OpenXmlElement target)
    {
        if (host.Deck == null || brush is not { } f) return;
        End();
        edits.Apply(() =>
        {
            FormatPainter.Apply(target, f);
            selection.Primary = target;
        });
    }

    /// <summary>Puts the brush away (Esc, a click on empty space, another tool).</summary>
    public void End()
    {
        if (brush == null) return;
        brush = null;
        host.Surface.Cursor = null;
        host.SlideStatus();
    }

    public void Copy()
    {
        if (selection.Primary is not { } src) { host.Status(L.T("selectShapeFirst")); return; }
        copied = FormatPainter.Take(src);
        host.Status(L.T("formatCopied"));
    }

    public void Paste()
    {
        if (copied is not { } f) return;
        edits.OnShapes((el, _) => FormatPainter.Apply(el, f), el => ShapeStyle.CanFormatShape(el) || TextFormat.HasFormat(el));
    }
}
