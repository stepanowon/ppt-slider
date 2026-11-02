using System.Windows.Media;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using static PptSlider.Ooxml;

namespace PptSlider;

/// <summary>How a shape looks: its fill and outline (color, weight, dashes).</summary>
static class ShapeStyle
{
    public static OpenXmlElement? SpPr(OpenXmlElement? el) => X.Child(el, "spPr");

    /// <summary>Fill and outline apply: shapes, lines and pictures (not groups, tables or charts).</summary>
    public static bool CanFormatShape(OpenXmlElement? el) => el?.LocalName is "sp" or "pic" or "cxnSp";

    /// <summary>Solid fill (null: no fill).</summary>
    public static void SetFill(OpenXmlElement el, Color? c)
    {
        if (el.LocalName != "cxnSp" && SpPr(el) is { } s) Put(s, SpPrOrder, "solidFill", c is { } v ? Solid(v) : new A.NoFill());
    }

    static OpenXmlElement Ln(OpenXmlElement spPr)
    {
        if (X.Child(spPr, "ln") is { } ln) return ln;
        var n = new A.Outline();
        Put(spPr, SpPrOrder, "ln", n);
        return n;
    }

    /// <summary>Outline color (null: no outline).</summary>
    public static void SetLineColor(OpenXmlElement el, Color? c)
    {
        if (SpPr(el) is { } s) Put(Ln(s), LnOrder, "solidFill", c is { } v ? Solid(v) : new A.NoFill());
    }

    /// <summary>Outline weight in points; an outline that was off is turned on (black, as PowerPoint does).</summary>
    public static void SetLineWidth(OpenXmlElement el, double pt)
    {
        if (SpPr(el) is not { } s) return;
        var ln = Ln(s);
        SetAttr(ln, "w", ((long)Math.Round(pt * 12700)).ToString());
        bool styled = X.Long(X.Child(X.Child(el, "style"), "lnRef"), "idx") is > 0;
        if (X.Child(ln, "noFill") != null || (!styled && Take(ln, LnOrder, "solidFill") == null)) Put(ln, LnOrder, "solidFill", Solid(Colors.Black));
    }

    /// <summary>"solid", "dash", "sysDot"...: the outline's dash pattern.</summary>
    public static void SetDash(OpenXmlElement el, string dash)
    {
        if (SpPr(el) is not { } s) return;
        var d = new A.PresetDash();
        SetAttr(d, "val", dash);
        Put(Ln(s), LnOrder, "prstDash", d);
    }
}
