using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace PptSlider;

/// <summary>An open .pptx held in memory. Edits change the OpenXML DOM directly so saving keeps everything we don't render.</summary>
public sealed class Deck : IDisposable
{
    const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

    public string Path { get; private set; }
    public PresentationDocument Doc { get; }
    public PresentationPart Pres => Doc.PresentationPart!;
    public SlideRenderer Renderer { get; }
    public bool Dirty { get; private set; }
    readonly Stack<(SlidePart part, string xml)> undo = new();

    Deck(string path, PresentationDocument doc)
    {
        Path = path;
        Doc = doc;
        Renderer = new SlideRenderer(Pres);
    }

    public static Deck Open(string path)
    {
        var ms = new MemoryStream();
        ms.Write(File.ReadAllBytes(path)); // in memory: the file stays unlocked and can be overwritten on save
        return new Deck(path, PresentationDocument.Open(ms, true));
    }

    public void Dispose() => Doc.Dispose();

    P.SlideIdList IdList => Pres.Presentation!.SlideIdList ??= new P.SlideIdList();

    public List<SlidePart> Slides => SlidesOf(Pres);

    static List<SlidePart> SlidesOf(PresentationPart pres) =>
        pres.Presentation!.SlideIdList?.Elements<P.SlideId>().Select(s => (SlidePart)pres.GetPartById(s.RelationshipId!)).ToList() ?? [];

    // ---------- undo ----------

    /// <summary>Call before mutating a slide.</summary>
    public void Snapshot(SlidePart sp)
    {
        undo.Push((sp, sp.Slide!.OuterXml));
        Dirty = true;
    }

    public SlidePart? Undo()
    {
        while (undo.TryPop(out var u))
        {
            if (!Slides.Contains(u.part)) continue; // slide was deleted since
            u.part.Slide = new P.Slide(u.xml);
            return u.part;
        }
        return null;
    }

    // ---------- save ----------

    /// <summary>Writes the deck to <paramref name="path"/>; when <paramref name="ink"/> is given each slide's strokes are baked in as a transparent picture.</summary>
    public void Save(string path, IReadOnlyDictionary<SlidePart, StrokeCollection>? ink)
    {
        var tmp = path + ".tmp";
        using (var clone = (PresentationDocument)Doc.Clone(tmp, true))
        {
            if (ink != null)
            {
                var src = Slides;
                var dst = SlidesOf(clone.PresentationPart!);
                for (int i = 0; i < src.Count; i++)
                    if (ink.TryGetValue(src[i], out var strokes) && strokes.Count > 0)
                        AddInkPicture(dst[i], strokes);
            }
        }
        File.Move(tmp, path, true); // never leave a half-written file in place of the original
        if (ink == null) { Path = path; Dirty = false; }
    }

    void AddInkPicture(SlidePart sp, StrokeCollection strokes)
    {
        double w = Renderer.Width, h = Renderer.Height;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            foreach (var s in strokes)
            {
                var st = s.Clone();
                if (st.DrawingAttributes.IsHighlighter) // bake highlighter transparency into the color
                {
                    st.DrawingAttributes.IsHighlighter = false;
                    var c = st.DrawingAttributes.Color;
                    st.DrawingAttributes.Color = Color.FromArgb(0x70, c.R, c.G, c.B);
                }
                st.Draw(dc);
            }
        var bmp = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        var img = sp.AddImagePart(ImagePartType.Png);
        using (var s = img.GetStream(FileMode.Create)) enc.Save(s);

        var tree = sp.Slide!.CommonSlideData!.ShapeTree!;
        long cx = (long)(w * SlideRenderer.Emu), cy = (long)(h * SlideRenderer.Emu);
        tree.Append(new P.Picture($"""
            <p:pic {Ns}><p:nvPicPr><p:cNvPr id="{NextId(sp)}" name="판서"/><p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>
            <p:blipFill><a:blip r:embed="{sp.GetIdOfPart(img)}"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>
            <p:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>
            """));
    }

    static uint NextId(SlidePart sp) =>
        sp.Slide!.Descendants<P.NonVisualDrawingProperties>().Select(p => p.Id?.Value ?? 0).DefaultIfEmpty(0u).Max() + 1;

    // ---------- shape edits ----------

    public static bool HasText(OpenXmlElement shape) => shape.LocalName == "sp";

    public static string GetText(OpenXmlElement shape) =>
        string.Join("\n", (X.Child(shape, "txBody")?.Elements<A.Paragraph>() ?? []).Select(p =>
            string.Concat(p.ChildElements.Select(c => c switch { A.Run r => r.Text?.Text, A.Field f => f.Text?.Text, A.Break => "\n", _ => "" }))));

    /// <summary>Replaces the text, one paragraph per line, keeping each paragraph's and its first run's formatting.</summary>
    public static void SetText(OpenXmlElement shape, string text)
    {
        var tx = X.Child(shape, "txBody") as P.TextBody;
        if (tx == null) { tx = new P.TextBody(new A.BodyProperties(), new A.ListStyle()); shape.Append(tx); }
        var old = tx.Elements<A.Paragraph>().ToList();
        foreach (var o in old) o.Remove();
        var lines = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var tpl = old.Count > 0 ? old[Math.Min(i, old.Count - 1)] : null;
            var p = new A.Paragraph();
            if (tpl?.ParagraphProperties is { } pp) p.Append(pp.CloneNode(true));
            var rPr = tpl?.Descendants<A.RunProperties>().FirstOrDefault();
            var endPr = tpl?.GetFirstChild<A.EndParagraphRunProperties>();
            if (lines[i].Length > 0)
            {
                var r = new A.Run();
                if (rPr != null) r.Append(rPr.CloneNode(true));
                else if (endPr != null) r.Append(Copy<A.RunProperties>(endPr));
                r.Append(new A.Text(lines[i]));
                p.Append(r);
            }
            if (endPr != null) p.Append(endPr.CloneNode(true));
            else if (rPr != null) p.Append(Copy<A.EndParagraphRunProperties>(rPr));
            tx.Append(p);
        }
    }

    static T Copy<T>(OpenXmlElement from) where T : OpenXmlCompositeElement, new()
    {
        var t = new T();
        t.SetAttributes(from.GetAttributes());
        foreach (var c in from.ChildElements) t.Append(c.CloneNode(true));
        return t;
    }

    /// <summary>Applies <paramref name="f"/> to every run (and end-of-paragraph) property set in the shape.</summary>
    public static void EditRuns(OpenXmlElement shape, Action<OpenXmlCompositeElement> f)
    {
        foreach (var p in X.Child(shape, "txBody")?.Elements<A.Paragraph>() ?? [])
        {
            foreach (var r in p.Elements<A.Run>())
                f(r.RunProperties ??= new A.RunProperties());
            var end = p.GetFirstChild<A.EndParagraphRunProperties>();
            if (end == null) p.Append(end = new A.EndParagraphRunProperties());
            f(end);
        }
    }

    public static string? FirstRun(OpenXmlElement shape, string attr) =>
        X.Child(shape, "txBody")?.Descendants().Where(e => e.LocalName is "rPr" or "endParaRPr").Select(e => X.Attr(e, attr)).FirstOrDefault(v => v != null);

    public static string? FirstRunFont(OpenXmlElement shape) =>
        X.Child(shape, "txBody")?.Descendants().Where(e => e.LocalName == "latin").Select(e => X.Attr(e, "typeface")).FirstOrDefault(v => v != null && !v.StartsWith('+'));

    public static void SetAttr(OpenXmlElement e, string name, string? value)
    {
        if (value == null) e.RemoveAttribute(name, "");
        else e.SetAttribute(new OpenXmlAttribute(name, "", value));
    }

    public static void SetColor(OpenXmlCompositeElement rPr, Color c)
    {
        foreach (var f in rPr.ChildElements.Where(e => e.LocalName is "solidFill" or "noFill" or "gradFill").ToList()) f.Remove();
        var fill = new A.SolidFill(new A.RgbColorModelHex { Val = $"{c.R:X2}{c.G:X2}{c.B:X2}" });
        // schema order: ln comes before the fill
        if (X.Child(rPr, "ln") is { } ln) ln.InsertAfterSelf(fill); else rPr.PrependChild(fill);
    }

    public static void SetFont(OpenXmlCompositeElement rPr, string face)
    {
        foreach (var f in rPr.ChildElements.Where(e => e.LocalName is "latin" or "ea").ToList()) f.Remove();
        var before = rPr.ChildElements.FirstOrDefault(e => e.LocalName is "cs" or "sym" or "hlinkClick" or "hlinkMouseOver" or "rtl" or "extLst");
        foreach (var el in new OpenXmlElement[] { new A.LatinFont { Typeface = face }, new A.EastAsianFont { Typeface = face } })
            if (before != null) before.InsertBeforeSelf(el); else rPr.Append(el);
    }

    public static void SetAlign(OpenXmlElement shape, string algn)
    {
        foreach (var p in X.Child(shape, "txBody")?.Elements<A.Paragraph>() ?? [])
            SetAttr(p.ParagraphProperties ??= new A.ParagraphProperties(), "algn", algn);
    }

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
        off.X = (long)(r.X * SlideRenderer.Emu); off.Y = (long)(r.Y * SlideRenderer.Emu);
        ext.Cx = (long)(Math.Max(1, r.Width) * SlideRenderer.Emu); ext.Cy = (long)(Math.Max(1, r.Height) * SlideRenderer.Emu);
    }

    public static OpenXmlElement AddTextBox(SlidePart sp, Rect r, string text)
    {
        var shape = new P.Shape($"""
            <p:sp {Ns}><p:nvSpPr><p:cNvPr id="{NextId(sp)}" name="TextBox"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
            <p:spPr><a:xfrm><a:off x="{(long)(r.X * SlideRenderer.Emu)}" y="{(long)(r.Y * SlideRenderer.Emu)}"/><a:ext cx="{(long)(r.Width * SlideRenderer.Emu)}" cy="{(long)(r.Height * SlideRenderer.Emu)}"/></a:xfrm>
            <a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>
            <p:txBody><a:bodyPr wrap="square"><a:spAutoFit/></a:bodyPr><a:lstStyle/><a:p><a:r><a:rPr lang="ko-KR" sz="2400"/><a:t></a:t></a:r></a:p></p:txBody></p:sp>
            """);
        shape.Descendants<A.Text>().First().Text = text;
        sp.Slide!.CommonSlideData!.ShapeTree!.Append(shape);
        return shape;
    }

    // ---------- slide edits ----------

    public SlidePart NewSlide(int after)
    {
        var layout = Slides.ElementAtOrDefault(after)?.SlideLayoutPart ?? Pres.SlideMasterParts.First().SlideLayoutParts.First();
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

    public void DeleteSlide(int i)
    {
        var sid = IdList.Elements<P.SlideId>().ElementAt(i);
        SectionEntry(sid.Id!.Value)?.Remove();
        Pres.DeletePart(sid.RelationshipId!);
        sid.Remove();
        Dirty = true;
    }
}
