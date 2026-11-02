using System.Windows.Media;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using static PptSlider.Ooxml;

namespace PptSlider;

/// <summary>Paragraph and character formatting of a shape's, a cell's or a whole table's text.</summary>
static class TextFormat
{
    /// <summary>The text bodies formatting applies to: a shape's own, or every cell of a table.</summary>
    public static List<OpenXmlElement> TextBodies(OpenXmlElement el) =>
        el.LocalName == "graphicFrame" ? el.Descendants().Where(e => e.LocalName == "txBody" && e.Parent?.LocalName == "tc").ToList()
        : X.Child(el, "txBody") is { } t ? [t] : [];

    public static List<A.Paragraph> Paragraphs(OpenXmlElement el) => TextBodies(el).SelectMany(t => t.Elements<A.Paragraph>()).ToList();

    /// <summary>Text formatting (font, bold, bullets...) applies: a text shape, a cell, or a whole table.</summary>
    public static bool HasFormat(OpenXmlElement? el) => el != null && (TextContent.HasText(el) || Tables.IsTable(el));

    // ---------- characters ----------

    /// <summary>Applies <paramref name="f"/> to every run (and end-of-paragraph) property set in the shape.</summary>
    public static void EditRuns(OpenXmlElement shape, Action<OpenXmlCompositeElement> f)
    {
        foreach (var p in Paragraphs(shape))
        {
            foreach (var r in p.Elements<A.Run>())
                f(r.RunProperties ??= new A.RunProperties());
            var end = p.GetFirstChild<A.EndParagraphRunProperties>();
            if (end == null) p.Append(end = new A.EndParagraphRunProperties());
            f(end);
        }
    }

    public static string? FirstRun(OpenXmlElement shape, string attr) =>
        TextBodies(shape).SelectMany(t => t.Descendants()).Where(e => e.LocalName is "rPr" or "endParaRPr").Select(e => X.Attr(e, attr)).FirstOrDefault(v => v != null);

    public static string? FirstRunFont(OpenXmlElement shape) =>
        TextBodies(shape).SelectMany(t => t.Descendants()).Where(e => e.LocalName == "latin").Select(e => X.Attr(e, "typeface")).FirstOrDefault(v => v != null && !v.StartsWith('+'));

    public static void SetColor(OpenXmlCompositeElement rPr, Color c) => Put(rPr, RPrOrder, "solidFill", Solid(c));

    public static void SetFont(OpenXmlCompositeElement rPr, string face)
    {
        Put(rPr, RPrOrder, "latin", new A.LatinFont { Typeface = face });
        Put(rPr, RPrOrder, "ea", new A.EastAsianFont { Typeface = face });
    }

    /// <summary>Text highlight color (null: none).</summary>
    public static void SetHighlight(OpenXmlCompositeElement rPr, Color? c) =>
        Put(rPr, RPrOrder, "highlight", c is { } v ? new A.Highlight(new A.RgbColorModelHex { Val = Hex(v) }) : null);

    public static void SetHighlight(OpenXmlCompositeElement rPr, string? hex) =>
        SetHighlight(rPr, hex is { Length: 6 } ? Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16)) : null);

    // ---------- paragraphs ----------

    static A.ParagraphProperties PPr(A.Paragraph p) => p.ParagraphProperties ??= new A.ParagraphProperties();

    public static void SetAlign(OpenXmlElement shape, string algn)
    {
        foreach (var p in Paragraphs(shape)) SetAlign(p, algn);
    }

    public static void SetAlign(A.Paragraph p, string algn) => SetAttr(PPr(p), "algn", algn);

    /// <summary>
    /// "buChar" (•), "buAutoNum" (1. 2. 3.) or null (no bullet). A bullet gets a hanging indent when the paragraph has none,
    /// as PowerPoint does; taking it away puts the text back at its level's start.
    /// </summary>
    public static void SetBullet(A.Paragraph p, string? kind)
    {
        var pPr = PPr(p);
        int lvl = (int)(X.Long(pPr, "lvl") ?? 0);
        if (kind == null)
        {
            Put(pPr, PPrOrder, "buFont", null);
            Put(pPr, PPrOrder, "buNone", new A.NoBullet());
            SetAttr(pPr, "marL", (lvl * 457200).ToString());
            SetAttr(pPr, "indent", "0");
            return;
        }
        if (kind == "buChar")
        {
            Put(pPr, PPrOrder, "buFont", new A.BulletFont { Typeface = "Arial", PitchFamily = 34, CharacterSet = 0 });
            Put(pPr, PPrOrder, "buNone", new A.CharacterBullet { Char = "•" });
        }
        else
        {
            Put(pPr, PPrOrder, "buFont", null);
            var num = new A.AutoNumberedBullet();
            SetAttr(num, "type", "arabicPeriod");
            Put(pPr, PPrOrder, "buNone", num);
        }
        if (X.Attr(pPr, "marL") is null or "0")
        {
            SetAttr(pPr, "marL", (342900 + lvl * 457200).ToString());
            SetAttr(pPr, "indent", "-342900");
        }
    }

    /// <summary>One list level in (+1) or out (-1); an explicit left margin moves with it.</summary>
    public static void Indent(A.Paragraph p, int delta)
    {
        var pPr = PPr(p);
        long was = X.Long(pPr, "lvl") ?? 0, lvl = Math.Clamp(was + delta, 0, 8);
        if (lvl == was) return;
        SetAttr(pPr, "lvl", lvl == 0 ? null : lvl.ToString());
        if (X.Long(pPr, "marL") is { } m) SetAttr(pPr, "marL", Math.Max(0, m + (lvl - was) * 457200).ToString());
    }

    /// <summary>Line spacing as a multiple of single spacing (1, 1.5, 2...).</summary>
    public static void SetLineSpacing(A.Paragraph p, double multiple) =>
        Put(PPr(p), PPrOrder, "lnSpc", new A.LineSpacing(new A.SpacingPercent { Val = (int)Math.Round(multiple * 100000) }));
}
