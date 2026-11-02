using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using static PptSlider.Ooxml;

namespace PptSlider;

/// <summary>
/// The text of shapes, table cells and notes: reading it, replacing it, and applying an in-place edit so that every
/// character keeps the formatting it had (paragraph and character formatting itself: <see cref="TextFormat"/>).
/// </summary>
static class TextContent
{
    /// <summary>The speaker notes: the text of the notes page's body placeholder ("" when there are none).</summary>
    public static string Notes(SlidePart sp)
    {
        var shapes = sp.NotesSlidePart?.NotesSlide?.CommonSlideData?.ShapeTree?.Elements<P.Shape>() ?? [];
        var body = shapes.FirstOrDefault(s =>
            s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value == P.PlaceholderValues.Body);
        return body == null ? "" : GetText(body).Trim();
    }

    /// <summary>A shape or table cell whose own text can be edited in place.</summary>
    public static bool HasText(OpenXmlElement shape) => shape.LocalName is "sp" or "tc";

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

    /// <summary>
    /// Bold / italic / underline, font (the editor's font family, first name = the face), size (points as drawn,
    /// i.e. after <see cref="FontScale"/>), strikethrough, color and highlight (RRGGBB) of one character as the editor shows it.
    /// </summary>
    public readonly record struct Fmt(bool B, bool I, bool U, string? Font = null, double Size = 0, bool S = false, string? Color = null, string? Hl = null);

    /// <summary>
    /// Formatting the editor changed on a character (null: as its run says). <see cref="Size"/> is in points as stored;
    /// <see cref="Hl"/> "" takes the highlight away.
    /// </summary>
    readonly record struct Ovr(bool? B, bool? I, bool? U, string? Font = null, double? Size = null, bool? S = null, string? Color = null, string? Hl = null)
    {
        public bool Any => B != null || I != null || U != null || Font != null || Size != null || S != null || Color != null || Hl != null;

        public void Apply(A.RunProperties r)
        {
            if (B is { } b) SetAttr(r, "b", b ? "1" : "0");
            if (I is { } i) SetAttr(r, "i", i ? "1" : "0");
            if (U is { } u) SetAttr(r, "u", u ? "sng" : "none");
            if (S is { } s) SetAttr(r, "strike", s ? "sngStrike" : "noStrike");
            if (Size is { } sz) SetAttr(r, "sz", ((int)Math.Round(sz * 100)).ToString());
            if (Color is { Length: 6 } c) Put(r, RPrOrder, "solidFill", new A.SolidFill(new A.RgbColorModelHex { Val = c }));
            if (Hl is { } h) TextFormat.SetHighlight(r, h);
            if (Font is { } f) TextFormat.SetFont(r, f.Split(',')[0].Trim());
        }
    }

    /// <summary>The shrink-on-overflow scale (normAutofit fontScale) the shape's text is drawn with.</summary>
    public static double FontScale(OpenXmlElement shape) =>
        (X.Long(X.Child(X.Child(X.Child(shape, "txBody"), "bodyPr"), "normAutofit"), "fontScale") ?? 100000) / 100000.0;

    /// <summary>One character of a shape's text and the run / field / line break it came from (null: a paragraph end).</summary>
    readonly record struct Ch(char C, OpenXmlElement? Src, int Para, bool Typed = false, Ovr O = default);

    static List<Ch> Chars(List<A.Paragraph> paras)
    {
        var list = new List<Ch>();
        for (int i = 0; i < paras.Count; i++)
        {
            if (i > 0) list.Add(new('\n', null, i - 1));
            foreach (var e in paras[i].ChildElements)
                switch (e)
                {
                    case A.Run r: foreach (var c in r.Text?.Text ?? "") list.Add(new(c, r, i)); break;
                    case A.Field f: foreach (var c in f.Text?.Text ?? "") list.Add(new(c, f, i)); break;
                    case A.Break: list.Add(new('\v', e, i)); break;
                }
        }
        return list;
    }

    static List<Ch> Chars(OpenXmlElement shape) => Chars(X.Child(shape, "txBody")?.Elements<A.Paragraph>().ToList() ?? []);

    /// <summary>For each character of <see cref="GetEditText"/>: the run / field / line break it belongs to (null: paragraph end).</summary>
    internal static List<OpenXmlElement?> CharSources(OpenXmlElement shape) => Chars(shape).Select(c => c.Src).ToList();

    /// <summary>The text the in-place editor works on: paragraphs joined by '\n', line breaks (a:br) as '\v'.</summary>
    public static string GetEditText(OpenXmlElement shape) => new(Chars(shape).Select(c => c.C).ToArray());

    /// <summary>
    /// Applies an edit of <see cref="GetEditText"/> keeping the formatting: unchanged characters keep their run,
    /// typed text takes the formatting of the text it replaces or follows (as in PowerPoint),
    /// a new paragraph copies the one it was split from, and untouched paragraphs are left as they are.
    /// <paramref name="fmt"/> (per character of <paramref name="text"/>) and <paramref name="oldFmt"/> (per character of the
    /// original text) are what the editor showed after and before: where they differ, bold / italic / underline / font / size is set on the run.
    /// </summary>
    /// <param name="scale">The font scale the editor showed the text at; default: the shape's <see cref="FontScale"/>.</param>
    public static void SetEditText(OpenXmlElement shape, string text, IReadOnlyList<Fmt>? fmt = null, IReadOnlyList<Fmt>? oldFmt = null, double? scale = null)
    {
        var tx = X.Child(shape, "txBody");
        var paras = tx?.Elements<A.Paragraph>().ToList() ?? [];
        if (paras.Count == 0) { SetText(shape, text.Replace('\v', '\n')); return; }
        var old = Chars(paras);
        int n = old.Count, pre = 0, suf = 0;
        while (pre < n && pre < text.Length && old[pre].C == text[pre]) pre++;
        while (suf < n - pre && suf < text.Length - pre && old[n - 1 - suf].C == text[text.Length - 1 - suf]) suf++;

        // the paragraph the edit starts in, and the formatting typed text takes
        int at = pre == 0 ? 0 : old[pre - 1].Src == null ? old[pre - 1].Para + 1 : old[pre - 1].Para;
        bool Styled(int i) => i >= 0 && i < n && old[i].Para == at && old[i].Src is A.Run or A.Field;
        int styleAt = Styled(pre) && pre < n - suf ? pre // replacing a selection: its first character
            : Styled(pre - 1) ? pre - 1 : Styled(pre) ? pre : -1;
        var style = styleAt >= 0 ? old[styleAt].Src : null;

        var items = old.Take(pre)
            .Concat(text[pre..(text.Length - suf)].Select(c => new Ch(c, style, at, Typed: true)))
            .Concat(old.Skip(n - suf)).ToList();
        if (fmt != null && oldFmt != null)
        {
            double s = scale ?? FontScale(shape);
            for (int k = 0; k < items.Count; k++)
            {
                int was = k < pre ? k : k >= text.Length - suf ? k - text.Length + n : styleAt; // the original character it compares to
                Fmt a = was >= 0 ? oldFmt[was] : default, b = fmt[k];
                // font and size only against a real character: an empty box's editor defaults are not a choice
                bool font = was >= 0 && b.Font != null && a.Font != b.Font, size = was >= 0 && b.Size > 0 && Math.Abs(a.Size - b.Size) > 0.01;
                bool color = was >= 0 && b.Color != null && a.Color != b.Color;
                items[k] = items[k] with { O = new Ovr(a.B != b.B ? b.B : null, a.I != b.I ? b.I : null, a.U != b.U ? b.U : null,
                    font ? b.Font : null, size ? b.Size / s : null, a.S != b.S ? b.S : null, color ? b.Color : null, a.Hl != b.Hl ? b.Hl ?? "" : null) };
            }
        }
        var counts = paras.Select((_, i) => old.Count(c => c.Para == i && c.Src != null)).ToList();

        var result = new List<A.Paragraph>();
        int cur = items.Count > 0 && !items[0].Typed ? items[0].Para : at;
        var line = new List<Ch>();
        foreach (var it in items)
        {
            if (it.C != '\n') { line.Add(it); continue; }
            result.Add(BuildParagraph(paras[cur], cur, line, counts[cur]));
            line.Clear();
            if (!it.Typed) cur = it.Para + 1; // a typed Enter splits: both halves copy the same paragraph
        }
        result.Add(BuildParagraph(paras[cur], cur, line, counts[cur]));

        foreach (var p in paras) p.Remove();
        foreach (var p in result) tx!.Append(p);
    }

    static A.Paragraph BuildParagraph(A.Paragraph tpl, int index, List<Ch> line, int count)
    {
        if (line.Count == count && line.All(c => !c.Typed && c.Para == index && !c.O.Any)) return (A.Paragraph)tpl.CloneNode(true);
        var endPr = tpl.GetFirstChild<A.EndParagraphRunProperties>();
        A.RunProperties? RPr(OpenXmlElement? src) =>
            src?.GetFirstChild<A.RunProperties>()?.CloneNode(true) as A.RunProperties ?? (endPr != null ? Copy<A.RunProperties>(endPr) : null);

        var p = new A.Paragraph();
        if (tpl.ParagraphProperties is { } pp) p.Append(pp.CloneNode(true));
        A.RunProperties? firstRPr = null;
        for (int i = 0; i < line.Count;)
        {
            var it = line[i];
            if (it.C == '\v')
            {
                if (!it.Typed) p.Append(it.Src!.CloneNode(true));
                else { var br = new A.Break(); if (RPr(it.Src) is { } r) br.Append(r); p.Append(br); }
                i++;
                continue;
            }
            int j = i;
            while (j < line.Count && line[j].C != '\v' && ReferenceEquals(line[j].Src, it.Src) && line[j].O == it.O
                   && (it.Src is not A.Field || line[j].Typed == it.Typed)) j++; // text typed next to a field must not swallow it
            var group = line.GetRange(i, j - i);
            var s = new string(group.Select(c => c.C).ToArray());
            if (it.Src is A.Field f && !it.O.Any && group.All(c => !c.Typed) && s == f.Text?.Text)
                p.Append(f.CloneNode(true)); // an untouched field (slide number, date) stays a field
            else
            {
                var run = new A.Run();
                var r = RPr(it.Src);
                if (it.O.Any) it.O.Apply(r ??= new A.RunProperties());
                if (r != null) { run.Append(r); firstRPr ??= r; }
                run.Append(new A.Text(s));
                p.Append(run);
            }
            i = j;
        }
        if (endPr != null) p.Append(endPr.CloneNode(true));
        else if (firstRPr != null) p.Append(Copy<A.EndParagraphRunProperties>(firstRPr));
        return p;
    }
}
