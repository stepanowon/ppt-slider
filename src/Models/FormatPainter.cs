using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using static PptSlider.Ooxml;

namespace PptSlider;

/// <summary>What the format painter carries: fill, outline, the shape style, and the first run's character formatting.</summary>
public sealed record ShapeFormat(OpenXmlElement? Fill, OpenXmlElement? Line, OpenXmlElement? Style, OpenXmlElement? Run, string Kind);

/// <summary>Taking one shape's look and giving it to another (Format Painter, Ctrl+Shift+C / V).</summary>
static class FormatPainter
{
    public static ShapeFormat Take(OpenXmlElement el) => new(
        Ooxml.Take(ShapeStyle.SpPr(el), SpPrOrder, "solidFill"), X.Child(ShapeStyle.SpPr(el), "ln")?.CloneNode(true), X.Child(el, "style")?.CloneNode(true),
        TextFormat.TextBodies(el).SelectMany(t => t.Descendants()).FirstOrDefault(e => e.LocalName is "rPr" or "endParaRPr")?.CloneNode(true), el.LocalName);

    static readonly string[] RunAttrs = ["b", "i", "u", "strike", "sz", "baseline", "spc", "cap"];

    /// <summary>Gives <paramref name="el"/> the format <see cref="Take(OpenXmlElement)"/> took: it then looks the same.</summary>
    public static void Apply(OpenXmlElement el, ShapeFormat f)
    {
        if (ShapeStyle.CanFormatShape(el) && f.Kind is "sp" or "pic" or "cxnSp" && ShapeStyle.SpPr(el) is { } spPr)
        {
            // without its own fill / outline the source showed its style's, or none
            bool styleFill = X.Long(X.Child(f.Style, "fillRef"), "idx") is > 0, styleLine = X.Long(X.Child(f.Style, "lnRef"), "idx") is > 0;
            if (el.LocalName != "cxnSp")
                Put(spPr, SpPrOrder, "solidFill", f.Fill?.CloneNode(true) ?? (styleFill ? null : new A.NoFill()));
            Put(spPr, SpPrOrder, "ln", f.Line?.CloneNode(true) ?? (styleLine ? null : new A.Outline(new A.NoFill())));
            if (el.LocalName is "sp" or "cxnSp")
            {
                X.Child(el, "style")?.Remove();
                if (f.Style != null) spPr.InsertAfterSelf(f.Style.CloneNode(true));
            }
        }
        if (f.Run is { } from)
            TextFormat.EditRuns(el, to =>
            {
                foreach (var a in RunAttrs) SetAttr(to, a, X.Attr(from, a));
                foreach (var g in new[] { "solidFill", "highlight", "latin", "ea" }) Put(to, RPrOrder, g, Ooxml.Take(from, RPrOrder, g));
            });
    }
}
