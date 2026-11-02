using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;
using static PptSlider.Ooxml;

namespace PptSlider;

/// <summary>
/// An open .pptx held in memory: opening and saving it, its slides (add, duplicate, move, delete) and its edit history.
/// Edits change the OpenXML DOM directly so saving keeps everything we don't render; what they do to a slide's shapes
/// and text lives in <see cref="ShapeFactory"/>, <see cref="ShapeLayout"/>, <see cref="ShapeStyle"/>, <see cref="Tables"/>,
/// <see cref="TextContent"/>, <see cref="TextFormat"/>, <see cref="ShapeClipboard"/> and <see cref="FormatPainter"/>.
/// </summary>
public sealed class Deck : IDisposable
{
    public string Path { get; private set; }
    public PresentationDocument Doc { get; }
    public PresentationPart Pres => Doc.PresentationPart!;
    public SlideRenderer Renderer { get; }
    public EditHistory History { get; }
    public bool Dirty { get; private set; }

    Deck(string path, PresentationDocument doc)
    {
        Path = path;
        Doc = doc;
        Renderer = new SlideRenderer(Pres);
        History = new EditHistory(sp => Slides.Contains(sp));
        History.Changed += MarkDirty;
    }

    public static Deck Open(string path)
    {
        var ms = new MemoryStream();
        ms.Write(File.ReadAllBytes(path)); // in memory: the file stays unlocked and can be overwritten on save
        return new Deck(path, PresentationDocument.Open(ms, true));
    }

    /// <summary>Opens an autosaved <paramref name="copy"/> of <paramref name="path"/>: it counts as unsaved, and saving writes <paramref name="path"/>.</summary>
    public static Deck Recover(string path, string copy)
    {
        var d = Open(copy);
        d.Path = path;
        d.Dirty = true;
        return d;
    }

    public void Dispose() => Doc.Dispose();

    /// <summary>Something in the document changed since it was saved.</summary>
    public void MarkDirty() => Dirty = true;

    P.SlideIdList IdList => Pres.Presentation!.SlideIdList ??= new P.SlideIdList();

    public List<SlidePart> Slides => SlidesOf(Pres);

    static List<SlidePart> SlidesOf(PresentationPart pres) =>
        pres.Presentation!.SlideIdList?.Elements<P.SlideId>().Select(s => (SlidePart)pres.GetPartById(s.RelationshipId!)).ToList() ?? [];

    // ---------- save ----------

    /// <summary>Writes the deck to <paramref name="path"/>, which becomes its file.</summary>
    public void Save(string path)
    {
        SaveCopy(path);
        Path = path;
        Dirty = false;
    }

    /// <summary>Writes the deck as it is to <paramref name="path"/> without making it the deck's file or clearing <see cref="Dirty"/> (autosave).</summary>
    public void SaveCopy(string path)
    {
        var tmp = path + ".tmp";
        using (var copy = Doc.Clone(tmp, true)) DropUnusedImages(copy);
        File.Move(tmp, path, true); // never leave a half-written file in place of the original
    }

    /// <summary>
    /// Pictures a slide no longer shows (inserted then undone, or deleted) keep their image part in memory, so undo can
    /// bring them back; the file written leaves out every slide image nothing in the slide refers to.
    /// </summary>
    static void DropUnusedImages(PresentationDocument doc)
    {
        foreach (var sp in SlidesOf(doc.PresentationPart!))
        {
            var used = sp.Slide!.Descendants().SelectMany(e => e.GetAttributes())
                .Where(a => a.NamespaceUri.Contains("relationships", StringComparison.Ordinal)).Select(a => a.Value).ToHashSet();
            foreach (var rel in sp.Parts.Where(r => r.OpenXmlPart is ImagePart && !used.Contains(r.RelationshipId)).ToList())
                sp.DeletePart(rel.RelationshipId);
        }
    }

    // ---------- slides ----------

    /// <summary>The slide's p:sldId id: stable while the deck is edited and across saves, unlike its position.</summary>
    public uint IdOf(SlidePart sp) =>
        IdList.Elements<P.SlideId>().FirstOrDefault(s => s.RelationshipId == Pres.GetIdOfPart(sp))?.Id?.Value ?? 0;

    /// <param name="blank">An empty page to draw on: the master's "Blank" layout when it has one.</param>
    public SlidePart NewSlide(int after, bool blank = false)
    {
        var near = Slides.ElementAtOrDefault(after)?.SlideLayoutPart;
        var blankLayout = blank
            ? (near?.SlideMasterPart ?? Pres.SlideMasterParts.First()).SlideLayoutParts.FirstOrDefault(l => l.SlideLayout?.Type?.Value == P.SlideLayoutValues.Blank)
            : null;
        var layout = blankLayout ?? near ?? Pres.SlideMasterParts.First().SlideLayoutParts.First();
        var part = Pres.AddNewPart<SlidePart>();
        part.Slide = new P.Slide($"""
            <p:sld {Ns}><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld>
            <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>
            """);
        part.AddPart(layout);
        Insert(part, after);
        return part;
    }

    public SlidePart Duplicate(int i)
    {
        var src = Slides[i];
        var part = Pres.AddNewPart<SlidePart>();
        foreach (var rel in src.Parts.Where(r => r.OpenXmlPart is not NotesSlidePart))
            part.AddPart(rel.OpenXmlPart, rel.RelationshipId);
        foreach (var ext in src.ExternalRelationships) part.AddExternalRelationship(ext.RelationshipType, ext.Uri, ext.Id);
        foreach (var h in src.HyperlinkRelationships) part.AddHyperlinkRelationship(h.Uri, h.IsExternal, h.Id);
        part.Slide = (P.Slide)src.Slide!.CloneNode(true);
        Insert(part, i);
        return part;
    }

    void Insert(SlidePart part, int after)
    {
        var ids = IdList.Elements<P.SlideId>().ToList();
        uint id = Math.Max(256, ids.Select(s => s.Id?.Value ?? 0).DefaultIfEmpty(0u).Max() + 1);
        var sid = new P.SlideId { Id = id, RelationshipId = Pres.GetIdOfPart(part) };
        var prev = ids.ElementAtOrDefault(after);
        if (prev != null) prev.InsertAfterSelf(sid); else IdList.Append(sid);
        // keep PowerPoint's section list (p14:sectionLst) consistent, or it asks to repair the file
        if (prev != null && SectionEntry(prev.Id!.Value) is { } entry)
        {
            var copy = entry.CloneNode(true);
            SetAttr(copy, "id", id.ToString());
            entry.InsertAfterSelf(copy);
        }
        Dirty = true;
    }

    OpenXmlElement? SectionEntry(uint id) =>
        Pres.Presentation!.Descendants().FirstOrDefault(e => e.LocalName == "sldId" && e.Parent?.Parent?.LocalName == "section" && X.Attr(e, "id") == id.ToString());

    /// <summary>Moves slide <paramref name="from"/> so it ends up at index <paramref name="to"/> (both in the order before the move).</summary>
    public void MoveSlide(int from, int to)
    {
        var ids = IdList.Elements<P.SlideId>().ToList();
        to = Math.Clamp(to, 0, ids.Count - 1);
        if (from == to || from < 0 || from >= ids.Count) return;
        var sid = ids[from];
        var entry = SectionEntry(sid.Id!.Value);
        sid.Remove();
        ids.RemoveAt(from);
        if (to == 0) ids[0].InsertBeforeSelf(sid); else ids[to - 1].InsertAfterSelf(sid);
        // sections list slides in order: the entry goes after the one of the slide now before it
        if (entry != null)
        {
            entry.Remove();
            if (to > 0 && SectionEntry(ids[to - 1].Id!.Value) is { } prev) prev.InsertAfterSelf(entry);
            else if (to == 0 && SectionEntry(ids[0].Id!.Value) is { } next) next.InsertBeforeSelf(entry);
        }
        Dirty = true;
    }

    public void DeleteSlide(int i)
    {
        var sid = IdList.Elements<P.SlideId>().ElementAt(i);
        SectionEntry(sid.Id!.Value)?.Remove();
        Pres.DeletePart(sid.RelationshipId!);
        sid.Remove();
        Dirty = true;
    }
}
