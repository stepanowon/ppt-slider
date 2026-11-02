using System.Windows;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using static PptSlider.Ooxml;

namespace PptSlider;

/// <summary>
/// Copied shapes: detached copies (so later edits don't change them) and the parts and links their relationship ids
/// point to on the slide they came from (pictures, charts, hyperlinks).
/// </summary>
public sealed record ShapeClip(Deck Owner, SlidePart From, List<OpenXmlElement> Shapes, Dictionary<string, OpenXmlPart> Parts, Dictionary<string, (Uri Uri, bool External)> Links);

/// <summary>Copying shapes and pasting them on a slide of the same deck, with their pictures and links.</summary>
static class ShapeClipboard
{
    static IEnumerable<(OpenXmlElement El, OpenXmlAttribute A)> RelAttrs(OpenXmlElement el) =>
        el.Descendants().Prepend(el).SelectMany(e => e.GetAttributes().Where(a => a.NamespaceUri == RelNs && !string.IsNullOrEmpty(a.Value)).Select(a => (e, a))).ToList();

    /// <param name="shapes">Top-level shapes of <paramref name="sp"/> and where they are drawn: a placeholder keeps that place.</param>
    public static ShapeClip Copy(Deck deck, SlidePart sp, IEnumerable<(OpenXmlElement El, Rect Bounds)> shapes)
    {
        var list = new List<OpenXmlElement>();
        var parts = new Dictionary<string, OpenXmlPart>();
        var links = new Dictionary<string, (Uri, bool)>();
        foreach (var (el, b) in shapes.OrderBy(s => s.El.Parent?.ChildElements.ToList().IndexOf(s.El) ?? 0)) // in drawing order
        {
            var c = el.CloneNode(true);
            if (c.LocalName != "AlternateContent" && ShapeLayout.XfrmOf(c) == null) ShapeLayout.SetBounds(c, b);
            list.Add(c);
            foreach (var (_, a) in RelAttrs(c))
            {
                if (sp.Parts.Where(p => p.RelationshipId == a.Value).Select(p => p.OpenXmlPart).FirstOrDefault() is { } part) parts[a.Value!] = part;
                else if (sp.HyperlinkRelationships.FirstOrDefault(h => h.Id == a.Value) is { } h) links[a.Value!] = (h.Uri, h.IsExternal);
            }
        }
        return new ShapeClip(deck, sp, list, parts, links);
    }

    /// <summary>
    /// Adds copies of the clip's shapes to <paramref name="target"/>, moved by (<paramref name="dx"/>, <paramref name="dy"/>),
    /// with new shape ids and relationships of the target slide. Shapes whose parts are gone are left out. Returns the new shapes.
    /// </summary>
    public static List<OpenXmlElement> Paste(Deck deck, SlidePart target, ShapeClip clip, double dx = 0, double dy = 0)
    {
        var added = new List<OpenXmlElement>();
        if (clip.Owner != deck) return added;
        foreach (var src in clip.Shapes)
        {
            var el = src.CloneNode(true);
            try
            {
                if (target != clip.From)
                {
                    var map = new Dictionary<string, string>();
                    foreach (var (e, a) in RelAttrs(el))
                    {
                        if (!map.TryGetValue(a.Value!, out var id))
                            map[a.Value!] = id = clip.Parts.TryGetValue(a.Value!, out var part)
                                ? target.Parts.Where(p => p.OpenXmlPart == part).Select(p => p.RelationshipId).FirstOrDefault() ?? target.CreateRelationshipToPart(part)
                                : clip.Links.TryGetValue(a.Value!, out var l) ? target.AddHyperlinkRelationship(l.Uri, l.External).Id
                                : a.Value!;
                        e.SetAttribute(new OpenXmlAttribute(a.Prefix, a.LocalName, a.NamespaceUri, id));
                    }
                }
            }
            catch (Exception) { continue; } // e.g. a picture whose slide was deleted since the copy
            uint next = SlideTree.NextId(target);
            foreach (var nv in el.Descendants().Where(e => e.LocalName == "cNvPr")) SetAttr(nv, "id", (next++).ToString());
            if ((dx != 0 || dy != 0) && X.Child(ShapeLayout.XfrmOf(el), "off") is { } off)
            {
                SetAttr(off, "x", ((X.Long(off, "x") ?? 0) + Emu(dx)).ToString());
                SetAttr(off, "y", ((X.Long(off, "y") ?? 0) + Emu(dy)).ToString());
            }
            SlideTree.AppendShape(target, el);
            added.Add(el);
        }
        if (added.Count > 0) deck.MarkDirty();
        return added;
    }
}
