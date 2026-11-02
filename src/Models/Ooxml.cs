using System.Windows.Media;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;

namespace PptSlider;

/// <summary>
/// Writing OpenXML the way PowerPoint expects it: namespaces, attributes, colors, and child elements in schema order.
/// PowerPoint repairs (or refuses) files whose child elements are out of order, so every child goes to its place.
/// </summary>
static class Ooxml
{
    public const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
    public const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static readonly string[][] PPrOrder =
        [["lnSpc"], ["spcBef"], ["spcAft"], ["buClrTx", "buClr"], ["buSzTx", "buSzPct", "buSzPts"], ["buFontTx", "buFont"],
         ["buNone", "buAutoNum", "buChar", "buBlip"], ["tabLst"], ["defRPr"], ["extLst"]];
    public static readonly string[][] RPrOrder =
        [["ln"], ["noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill"], ["effectLst", "effectDag"], ["highlight"],
         ["uLnTx", "uLn"], ["uFillTx", "uFill"], ["latin"], ["ea"], ["cs"], ["sym"], ["hlinkClick"], ["hlinkMouseOver"], ["rtl"], ["extLst"]];
    public static readonly string[][] SpPrOrder =
        [["xfrm"], ["custGeom", "prstGeom"], ["noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill"], ["ln"],
         ["effectLst", "effectDag"], ["scene3d"], ["sp3d"], ["extLst"]];
    public static readonly string[][] LnOrder = [["noFill", "solidFill", "gradFill", "pattFill"], ["prstDash", "custDash"], ["round", "bevel", "miter"], ["headEnd"], ["tailEnd"], ["extLst"]];

    static int Rank(string[][] order, string name) => Array.FindIndex(order, g => g.Contains(name));

    /// <summary>
    /// Puts <paramref name="child"/> where the schema wants it, replacing the children of <paramref name="group"/>'s
    /// group (e.g. any fill for "solidFill"); a null child only removes them.
    /// </summary>
    public static void Put(OpenXmlElement parent, string[][] order, string group, OpenXmlElement? child)
    {
        int rank = Rank(order, group);
        foreach (var old in parent.ChildElements.Where(e => Rank(order, e.LocalName) == rank).ToList()) old.Remove();
        if (child == null) return;
        if (parent.ChildElements.FirstOrDefault(e => Rank(order, e.LocalName) > rank) is { } before) before.InsertBeforeSelf(child);
        else parent.AppendChild(child);
    }

    /// <summary>The child of <paramref name="from"/> in <paramref name="group"/>'s group, copied (null when there is none).</summary>
    public static OpenXmlElement? Take(OpenXmlElement? from, string[][] order, string group) =>
        from?.ChildElements.FirstOrDefault(e => Rank(order, e.LocalName) == Rank(order, group))?.CloneNode(true);

    /// <summary>Sets an unqualified attribute; null removes it.</summary>
    public static void SetAttr(OpenXmlElement e, string name, string? value)
    {
        if (value == null) e.RemoveAttribute(name, "");
        else e.SetAttribute(new OpenXmlAttribute(name, "", value));
    }

    /// <summary>A <typeparamref name="T"/> with <paramref name="from"/>'s attributes and children (e.g. rPr → endParaRPr).</summary>
    public static T Copy<T>(OpenXmlElement from) where T : OpenXmlCompositeElement, new()
    {
        var t = new T();
        t.SetAttributes(from.GetAttributes());
        foreach (var c in from.ChildElements) t.Append(c.CloneNode(true));
        return t;
    }

    /// <summary>"RRGGBB".</summary>
    public static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    public static A.SolidFill Solid(Color c) => new(new A.RgbColorModelHex { Val = Hex(c) });

    /// <summary>EMU (as written in the file) from slide DIPs.</summary>
    public static long Emu(double dips) => (long)(dips * SlideRenderer.Emu);
}
