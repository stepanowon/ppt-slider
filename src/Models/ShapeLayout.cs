using System.Windows;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using static PptSlider.Ooxml;

namespace PptSlider;

/// <summary>Where a shape is: position and size, rotation, flips (its a:xfrm).</summary>
static class ShapeLayout
{
    public static OpenXmlElement? XfrmOf(OpenXmlElement el) =>
        el.LocalName == "graphicFrame" ? X.Child(el, "xfrm") : X.Child(X.Child(el, el.LocalName == "grpSp" ? "grpSpPr" : "spPr"), "xfrm");

    /// <summary>Moves/resizes a top-level shape. Placeholders without their own xfrm get one from their drawn bounds.</summary>
    public static void SetBounds(OpenXmlElement shape, Rect r)
    {
        OpenXmlElement? xfrm = shape.LocalName == "graphicFrame" ? X.Child(shape, "xfrm") : null;
        if (xfrm == null)
        {
            var owner = X.Child(shape, shape.LocalName == "grpSp" ? "grpSpPr" : "spPr");
            if (owner == null) return;
            xfrm = X.Child(owner, "xfrm");
            if (xfrm == null) { xfrm = new A.Transform2D(new A.Offset(), new A.Extents()); owner.InsertAt(xfrm, 0); }
        }
        var off = xfrm.GetFirstChild<A.Offset>()!; var ext = xfrm.GetFirstChild<A.Extents>()!;
        double min = shape.LocalName == "cxnSp" ? 0 : 1; // a straight line has no height (or width)
        long cx = Emu(Math.Max(min, r.Width)), cy = Emu(Math.Max(min, r.Height));
        // a table is drawn from its column widths and row heights: a new size scales them
        if (Tables.IsTable(shape) && ext.Cx?.Value is > 0 and var ocx && ext.Cy?.Value is > 0 and var ocy && (cx != ocx || cy != ocy))
            Tables.Scale(shape, (cx, ocx), (cy, ocy));
        off.X = Emu(r.X); off.Y = Emu(r.Y);
        ext.Cx = cx; ext.Cy = cy;
    }

    /// <summary>Rotation and flips are kept for shapes, lines and pictures.</summary>
    public static bool CanRotate(OpenXmlElement? el) => el?.LocalName is "sp" or "pic" or "cxnSp";

    /// <summary>Clockwise degrees (0 when not rotated).</summary>
    public static double Rotation(OpenXmlElement el) => (X.Long(XfrmOf(el), "rot") ?? 0) / 60000.0;

    /// <param name="bounds">Where the shape is drawn: a placeholder without its own position gets this one.</param>
    public static void SetRotation(OpenXmlElement el, Rect bounds, double degrees)
    {
        if (XfrmOf(el) == null) SetBounds(el, bounds);
        if (XfrmOf(el) is not { } x) return;
        long v = (long)Math.Round((degrees % 360 + 360) % 360 * 60000) % 21600000;
        SetAttr(x, "rot", v == 0 ? null : v.ToString());
    }

    public static void Flip(OpenXmlElement el, Rect bounds, bool horizontal)
    {
        if (XfrmOf(el) == null) SetBounds(el, bounds);
        if (XfrmOf(el) is not { } x) return;
        var name = horizontal ? "flipH" : "flipV";
        SetAttr(x, name, X.Attr(x, name) is "1" or "true" ? null : "1");
    }
}
