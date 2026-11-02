using System.IO;
using System.Windows;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace PptSlider;

/// <summary>Shapes, tables, paragraph and character formatting, arranging, copy / paste: edits of the slide XML.</summary>
public sealed partial class Deck
{
    const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // ---------- schema order ----------
    // PowerPoint repairs (or refuses) files whose child elements are out of order, so every child goes to its place.

    static readonly string[][] PPrOrder =
        [["lnSpc"], ["spcBef"], ["spcAft"], ["buClrTx", "buClr"], ["buSzTx", "buSzPct", "buSzPts"], ["buFontTx", "buFont"],
         ["buNone", "buAutoNum", "buChar", "buBlip"], ["tabLst"], ["defRPr"], ["extLst"]];
    static readonly string[][] RPrOrder =
        [["ln"], ["noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill"], ["effectLst", "effectDag"], ["highlight"],
         ["uLnTx", "uLn"], ["uFillTx", "uFill"], ["latin"], ["ea"], ["cs"], ["sym"], ["hlinkClick"], ["hlinkMouseOver"], ["rtl"], ["extLst"]];
    static readonly string[][] SpPrOrder =
        [["xfrm"], ["custGeom", "prstGeom"], ["noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill"], ["ln"],
         ["effectLst", "effectDag"], ["scene3d"], ["sp3d"], ["extLst"]];
    static readonly string[][] LnOrder = [["noFill", "solidFill", "gradFill", "pattFill"], ["prstDash", "custDash"], ["round", "bevel", "miter"], ["headEnd"], ["tailEnd"], ["extLst"]];

    static int Rank(string[][] order, string name) => Array.FindIndex(order, g => g.Contains(name));

    /// <summary>
    /// Puts <paramref name="child"/> where the schema wants it, replacing the children of <paramref name="group"/>'s
    /// group (e.g. any fill for "solidFill"); a null child only removes them.
    /// </summary>
    static void Put(OpenXmlElement parent, string[][] order, string group, OpenXmlElement? child)
    {
        int rank = Rank(order, group);
        foreach (var old in parent.ChildElements.Where(e => Rank(order, e.LocalName) == rank).ToList()) old.Remove();
        if (child == null) return;
        if (parent.ChildElements.FirstOrDefault(e => Rank(order, e.LocalName) > rank) is { } before) before.InsertBeforeSelf(child);
        else parent.AppendChild(child);
    }

    /// <summary>The child of <paramref name="from"/> in <paramref name="group"/>'s group, copied (null when there is none).</summary>
    static OpenXmlElement? Take(OpenXmlElement? from, string[][] order, string group) =>
        from?.ChildElements.FirstOrDefault(e => Rank(order, e.LocalName) == Rank(order, group))?.CloneNode(true);

    public static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
    static A.SolidFill Solid(Color c) => new(new A.RgbColorModelHex { Val = Hex(c) });

    /// <summary>Adds a shape to the top of the slide's shape tree (before its extLst, which must stay last).</summary>
    static void AppendShape(SlidePart sp, OpenXmlElement shape)
    {
        var tree = sp.Slide!.CommonSlideData!.ShapeTree!;
        if (X.Child(tree, "extLst") is { } ext) ext.InsertBeforeSelf(shape); else tree.AppendChild(shape);
    }

    // ---------- text of shapes and table cells ----------

    public static bool IsTable(OpenXmlElement? el) => el?.LocalName == "graphicFrame" && Tbl(el) != null;
    static OpenXmlElement? Tbl(OpenXmlElement frame) => frame.Descendants().FirstOrDefault(e => e.LocalName == "tbl");

    /// <summary>The text bodies formatting applies to: a shape's own, or every cell of a table.</summary>
    public static List<OpenXmlElement> TextBodies(OpenXmlElement el) =>
        el.LocalName == "graphicFrame" ? el.Descendants().Where(e => e.LocalName == "txBody" && e.Parent?.LocalName == "tc").ToList()
        : X.Child(el, "txBody") is { } t ? [t] : [];

    public static List<A.Paragraph> Paragraphs(OpenXmlElement el) => TextBodies(el).SelectMany(t => t.Elements<A.Paragraph>()).ToList();

    /// <summary>Text formatting (font, bold, bullets...) applies: a text shape, a cell, or a whole table.</summary>
    public static bool HasFormat(OpenXmlElement? el) => el != null && (HasText(el) || IsTable(el));

    // ---------- paragraphs ----------

    static A.ParagraphProperties PPr(A.Paragraph p) => p.ParagraphProperties ??= new A.ParagraphProperties();

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

    // ---------- characters ----------

    /// <summary>Text highlight color (null: none).</summary>
    public static void SetHighlight(OpenXmlCompositeElement rPr, Color? c) =>
        Put(rPr, RPrOrder, "highlight", c is { } v ? new A.Highlight(new A.RgbColorModelHex { Val = Hex(v) }) : null);

    public static void SetHighlight(OpenXmlCompositeElement rPr, string? hex) =>
        SetHighlight(rPr, hex is { Length: 6 } ? Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16)) : null);

    // ---------- shapes ----------

    /// <summary>The shapes the Shapes gallery offers: preset geometry names, plus "line" and "arrow".</summary>
    public static readonly string[] ShapeKinds =
        ["rect", "roundRect", "ellipse", "triangle", "rtTriangle", "diamond", "parallelogram", "trapezoid", "pentagon", "hexagon",
         "homePlate", "chevron", "star5", "rightArrow", "leftArrow", "upArrow", "downArrow", "line", "arrow"];

    static string Xfrm(Rect r, bool flipH = false, bool flipV = false) =>
        $"""<a:xfrm{(flipH ? " flipH=\"1\"" : "")}{(flipV ? " flipV=\"1\"" : "")}><a:off x="{(long)(r.X * SlideRenderer.Emu)}" y="{(long)(r.Y * SlideRenderer.Emu)}"/><a:ext cx="{(long)(r.Width * SlideRenderer.Emu)}" cy="{(long)(r.Height * SlideRenderer.Emu)}"/></a:xfrm>""";

    /// <summary>
    /// Adds a shape as PowerPoint draws a new one: theme accent fill and outline, white centered text.
    /// A line or arrow runs from one corner of <paramref name="r"/> to the other; the flips say which.
    /// </summary>
    public static OpenXmlElement AddShape(SlidePart sp, string kind, Rect r, bool flipH = false, bool flipV = false)
    {
        uint id = NextId(sp);
        OpenXmlElement el;
        if (kind is "line" or "arrow")
            el = new P.ConnectionShape($"""
                <p:cxnSp {Ns}><p:nvCxnSpPr><p:cNvPr id="{id}" name="{(kind == "arrow" ? "Straight Arrow Connector" : "Straight Connector")} {id - 1}"/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr>
                <p:spPr>{Xfrm(r, flipH, flipV)}<a:prstGeom prst="{(kind == "arrow" ? "straightConnector1" : "line")}"><a:avLst/></a:prstGeom><a:ln w="19050">{(kind == "arrow" ? "<a:tailEnd type=\"triangle\"/>" : "")}</a:ln></p:spPr>
                <p:style><a:lnRef idx="1"><a:schemeClr val="accent1"/></a:lnRef><a:fillRef idx="0"><a:schemeClr val="accent1"/></a:fillRef><a:effectRef idx="0"><a:schemeClr val="accent1"/></a:effectRef><a:fontRef idx="minor"><a:schemeClr val="tx1"/></a:fontRef></p:style></p:cxnSp>
                """);
        else
            el = new P.Shape($"""
                <p:sp {Ns}><p:nvSpPr><p:cNvPr id="{id}" name="Shape {id - 1}"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
                <p:spPr>{Xfrm(r, flipH, flipV)}<a:prstGeom prst="{kind}"><a:avLst/></a:prstGeom></p:spPr>
                <p:style><a:lnRef idx="2"><a:schemeClr val="accent1"><a:shade val="15000"/></a:schemeClr></a:lnRef><a:fillRef idx="1"><a:schemeClr val="accent1"/></a:fillRef><a:effectRef idx="0"><a:schemeClr val="accent1"/></a:effectRef><a:fontRef idx="minor"><a:schemeClr val="lt1"/></a:fontRef></p:style>
                <p:txBody><a:bodyPr rtlCol="0" anchor="ctr"/><a:lstStyle/><a:p><a:pPr algn="ctr"/><a:endParaRPr lang="ko-KR"/></a:p></p:txBody></p:sp>
                """);
        AppendShape(sp, el);
        return el;
    }

    static OpenXmlElement? SpPr(OpenXmlElement el) => X.Child(el, "spPr");

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

    static OpenXmlElement? XfrmOf(OpenXmlElement el) =>
        el.LocalName == "graphicFrame" ? X.Child(el, "xfrm") : X.Child(X.Child(el, el.LocalName == "grpSp" ? "grpSpPr" : "spPr"), "xfrm");

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

    // ---------- arranging ----------

    static bool IsShape(OpenXmlElement e) => e.LocalName is "sp" or "pic" or "cxnSp" or "grpSp" or "graphicFrame" or "contentPart" or "AlternateContent";

    /// <summary>"front", "back", "forward" (one step up) or "backward": the drawing order of <paramref name="els"/> (siblings).</summary>
    public static void Arrange(IReadOnlyCollection<OpenXmlElement> els, string how)
    {
        if (els.FirstOrDefault()?.Parent is not { } parent) return;
        var set = els.Where(e => e.Parent == parent).ToHashSet();
        var shapes = parent.ChildElements.Where(IsShape).ToList();
        if (shapes.Count < 2) return;
        var order = how switch
        {
            "front" => shapes.Where(s => !set.Contains(s)).Concat(shapes.Where(set.Contains)).ToList(),
            "back" => shapes.Where(set.Contains).Concat(shapes.Where(s => !set.Contains(s))).ToList(),
            _ => shapes.ToList(),
        };
        if (how == "forward")
            for (int i = order.Count - 2; i >= 0; i--)
                if (set.Contains(order[i]) && !set.Contains(order[i + 1])) (order[i], order[i + 1]) = (order[i + 1], order[i]);
        if (how == "backward")
            for (int i = 1; i < order.Count; i++)
                if (set.Contains(order[i]) && !set.Contains(order[i - 1])) (order[i], order[i - 1]) = (order[i - 1], order[i]);
        if (order.SequenceEqual(shapes)) return;
        var anchor = shapes[0].PreviousSibling(); // grpSpPr: shapes come right after it
        foreach (var s in shapes) s.Remove();
        foreach (var s in order)
        {
            if (anchor == null) parent.PrependChild(s); else anchor.InsertAfterSelf(s);
            anchor = s;
        }
    }

    // ---------- tables ----------

    static List<OpenXmlElement> Rows(OpenXmlElement tbl) => tbl.ChildElements.Where(e => e.LocalName == "tr").ToList();
    static List<OpenXmlElement> Cells(OpenXmlElement tr) => tr.ChildElements.Where(e => e.LocalName == "tc").ToList();

    /// <summary>The cells of a table frame, row by row, without the ones a merge covers.</summary>
    public static List<OpenXmlElement> TableCells(OpenXmlElement frame) =>
        Tbl(frame) is { } tbl ? Rows(tbl).SelectMany(Cells).Where(c => X.Attr(c, "hMerge") is not ("1" or "true") && X.Attr(c, "vMerge") is not ("1" or "true")).ToList() : [];

    public static (int Row, int Col) CellIndex(OpenXmlElement tc) => (Rows(tc.Parent!.Parent!).IndexOf(tc.Parent!), Cells(tc.Parent!).IndexOf(tc));

    public static OpenXmlElement Cell(OpenXmlElement frame, int row, int col) => Cells(Rows(Tbl(frame)!)[row])[col];

    public static (int Rows, int Cols) TableSize(OpenXmlElement frame) =>
        Tbl(frame) is { } tbl ? (Rows(tbl).Count, X.Child(tbl, "tblGrid")?.ChildElements.Count(c => c.LocalName == "gridCol") ?? 0) : (0, 0);

    /// <summary>A table in PowerPoint's default style (header row, banded rows), as wide as <paramref name="r"/>.</summary>
    public static OpenXmlElement AddTable(SlidePart sp, int rows, int cols, Rect r)
    {
        uint id = NextId(sp);
        long w = (long)(r.Width * SlideRenderer.Emu / cols), h = 370840;
        const string cell = """<a:tc><a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang="ko-KR"/></a:p></a:txBody><a:tcPr/></a:tc>""";
        var grid = string.Concat(Enumerable.Repeat($"""<a:gridCol w="{w}"/>""", cols));
        var trs = string.Concat(Enumerable.Repeat($"""<a:tr h="{h}">{string.Concat(Enumerable.Repeat(cell, cols))}</a:tr>""", rows));
        var frame = new P.GraphicFrame($$"""
            <p:graphicFrame {{Ns}}><p:nvGraphicFramePr><p:cNvPr id="{{id}}" name="Table {{id - 1}}"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp="1"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>
            <p:xfrm><a:off x="{{(long)(r.X * SlideRenderer.Emu)}}" y="{{(long)(r.Y * SlideRenderer.Emu)}}"/><a:ext cx="{{w * cols}}" cy="{{h * rows}}"/></p:xfrm>
            <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/table"><a:tbl><a:tblPr firstRow="1" bandRow="1"><a:tableStyleId>{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}</a:tableStyleId></a:tblPr>
            <a:tblGrid>{{grid}}</a:tblGrid>{{trs}}</a:tbl></a:graphicData></a:graphic></p:graphicFrame>
            """);
        AppendShape(sp, frame);
        return frame;
    }

    /// <summary>One empty paragraph that keeps the first one's settings and character formatting.</summary>
    static void EmptyText(OpenXmlElement? txBody)
    {
        if (txBody == null) return;
        var ps = txBody.Elements<A.Paragraph>().ToList();
        var first = ps.FirstOrDefault();
        var p = new A.Paragraph();
        if (first?.ParagraphProperties is { } pp) p.Append(pp.CloneNode(true));
        var end = first?.GetFirstChild<A.EndParagraphRunProperties>();
        var rPr = first?.Descendants<A.RunProperties>().FirstOrDefault();
        p.Append(end?.CloneNode(true) ?? (rPr != null ? Copy<A.EndParagraphRunProperties>(rPr) : new A.EndParagraphRunProperties()));
        foreach (var o in ps) o.Remove();
        txBody.AppendChild(p);
    }

    static void Grow(OpenXmlElement frame, string attr, long by)
    {
        if (X.Child(X.Child(frame, "xfrm"), "ext") is { } ext) SetAttr(ext, attr, Math.Max(1, (X.Long(ext, attr) ?? 0) + by).ToString());
    }

    static OpenXmlElement Blank(OpenXmlElement tc)
    {
        var c = tc.CloneNode(true);
        foreach (var a in new[] { "gridSpan", "rowSpan", "hMerge", "vMerge" }) SetAttr(c, a, null);
        EmptyText(X.Child(c, "txBody"));
        return c;
    }

    /// <summary>A copy of row <paramref name="row"/>, empty, above or below it. Returns its first cell.</summary>
    public static OpenXmlElement InsertRow(OpenXmlElement frame, int row, bool below)
    {
        var src = Rows(Tbl(frame)!)[row];
        var tr = src.CloneNode(false); // its attributes (height)
        foreach (var c in src.ChildElements)
            if (c.LocalName == "tc") tr.AppendChild(Blank(c));
            else if (c.LocalName != "extLst") tr.AppendChild(c.CloneNode(true)); // row ids (extLst) must stay unique
        if (below) src.InsertAfterSelf(tr); else src.InsertBeforeSelf(tr);
        Grow(frame, "cy", X.Long(tr, "h") ?? 0);
        return Cells(tr)[0];
    }

    /// <summary>An empty column left or right of <paramref name="col"/>, as wide as it; merged cells across it grow.</summary>
    public static void InsertColumn(OpenXmlElement frame, int col, bool right)
    {
        var tbl = Tbl(frame)!;
        var gcols = X.Child(tbl, "tblGrid")!.ChildElements.Where(e => e.LocalName == "gridCol").ToList();
        var gc = gcols[col].CloneNode(true);
        foreach (var e in gc.ChildElements.Where(e => e.LocalName == "extLst").ToList()) e.Remove();
        if (right) gcols[col].InsertAfterSelf(gc); else gcols[col].InsertBeforeSelf(gc);
        int k = right ? col + 1 : col;
        foreach (var tr in Rows(tbl))
        {
            var cells = Cells(tr);
            var tc = Blank(cells[col]);
            // inside a horizontal merge: the merge takes the new column in
            if (k < cells.Count && X.Attr(cells[k], "hMerge") is "1" or "true")
            {
                int o = k;
                while (o > 0 && X.Attr(cells[o], "hMerge") is "1" or "true") o--;
                SetAttr(cells[o], "gridSpan", ((X.Long(cells[o], "gridSpan") ?? 1) + 1).ToString());
                SetAttr(tc, "hMerge", "1");
            }
            if (k < cells.Count) cells[k].InsertBeforeSelf(tc); else cells[^1].InsertAfterSelf(tc);
        }
        Grow(frame, "cx", X.Long(gc, "w") ?? 0);
    }

    /// <summary>Deletes a row; false when it is the last one (delete the table instead).</summary>
    public static bool DeleteRow(OpenXmlElement frame, int row)
    {
        var rows = Rows(Tbl(frame)!);
        if (rows.Count <= 1) return false;
        var tr = rows[row];
        var cells = Cells(tr);
        for (int c = 0; c < cells.Count; c++)
        {
            var tc = cells[c];
            long span = X.Long(tc, "rowSpan") ?? 1;
            if (span > 1 && row + 1 < rows.Count && Cells(rows[row + 1]).ElementAtOrDefault(c) is { } next)
            {
                // the merge goes on from the next row, which takes over the text
                SetAttr(next, "vMerge", null);
                SetAttr(next, "rowSpan", span > 2 ? (span - 1).ToString() : null);
                SetAttr(next, "gridSpan", X.Attr(tc, "gridSpan"));
                if (X.Child(tc, "txBody") is { } tx && X.Child(next, "txBody") is { } old) { old.InsertBeforeSelf(tx.CloneNode(true)); old.Remove(); }
            }
            else if (X.Attr(tc, "vMerge") is "1" or "true")
                for (int r = row - 1; r >= 0; r--)
                    if (Cells(rows[r]).ElementAtOrDefault(c) is { } up && X.Attr(up, "vMerge") is not ("1" or "true"))
                    {
                        SetAttr(up, "rowSpan", (X.Long(up, "rowSpan") ?? 1) > 2 ? ((X.Long(up, "rowSpan") ?? 1) - 1).ToString() : null);
                        break;
                    }
        }
        tr.Remove();
        Grow(frame, "cy", -(X.Long(tr, "h") ?? 0));
        return true;
    }

    /// <summary>Deletes a column; false when it is the last one.</summary>
    public static bool DeleteColumn(OpenXmlElement frame, int col)
    {
        var tbl = Tbl(frame)!;
        var gcols = X.Child(tbl, "tblGrid")!.ChildElements.Where(e => e.LocalName == "gridCol").ToList();
        if (gcols.Count <= 1) return false;
        foreach (var tr in Rows(tbl))
        {
            var cells = Cells(tr);
            if (col >= cells.Count) continue;
            var tc = cells[col];
            long span = X.Long(tc, "gridSpan") ?? 1;
            if (span > 1 && col + 1 < cells.Count)
            {
                var next = cells[col + 1];
                SetAttr(next, "hMerge", null);
                SetAttr(next, "gridSpan", span > 2 ? (span - 1).ToString() : null);
                SetAttr(next, "rowSpan", X.Attr(tc, "rowSpan"));
                SetAttr(next, "vMerge", X.Attr(tc, "vMerge"));
                if (X.Child(tc, "txBody") is { } tx && X.Child(next, "txBody") is { } old) { old.InsertBeforeSelf(tx.CloneNode(true)); old.Remove(); }
            }
            else if (X.Attr(tc, "hMerge") is "1" or "true")
                for (int o = col - 1; o >= 0; o--)
                    if (X.Attr(cells[o], "hMerge") is not ("1" or "true"))
                    {
                        long s = X.Long(cells[o], "gridSpan") ?? 1;
                        SetAttr(cells[o], "gridSpan", s > 2 ? (s - 1).ToString() : null);
                        break;
                    }
            tc.Remove();
        }
        gcols[col].Remove();
        Grow(frame, "cx", -(X.Long(gcols[col], "w") ?? 0));
        return true;
    }

    // ---------- copy / paste ----------

    /// <summary>
    /// Copied shapes: detached copies (so later edits don't change them) and the parts and links their relationship ids
    /// point to on the slide they came from (pictures, charts, hyperlinks).
    /// </summary>
    public sealed record Clip(Deck Owner, SlidePart From, List<OpenXmlElement> Shapes, Dictionary<string, OpenXmlPart> Parts, Dictionary<string, (Uri Uri, bool External)> Links);

    static IEnumerable<(OpenXmlElement El, OpenXmlAttribute A)> RelAttrs(OpenXmlElement el) =>
        el.Descendants().Prepend(el).SelectMany(e => e.GetAttributes().Where(a => a.NamespaceUri == RelNs && !string.IsNullOrEmpty(a.Value)).Select(a => (e, a))).ToList();

    /// <param name="shapes">Top-level shapes of <paramref name="sp"/> and where they are drawn: a placeholder keeps that place.</param>
    public Clip Copy(SlidePart sp, IEnumerable<(OpenXmlElement El, Rect Bounds)> shapes)
    {
        var list = new List<OpenXmlElement>();
        var parts = new Dictionary<string, OpenXmlPart>();
        var links = new Dictionary<string, (Uri, bool)>();
        foreach (var (el, b) in shapes.OrderBy(s => s.El.Parent?.ChildElements.ToList().IndexOf(s.El) ?? 0)) // in drawing order
        {
            var c = el.CloneNode(true);
            if (c.LocalName != "AlternateContent" && XfrmOf(c) == null) SetBounds(c, b);
            list.Add(c);
            foreach (var (_, a) in RelAttrs(c))
            {
                if (sp.Parts.Where(p => p.RelationshipId == a.Value).Select(p => p.OpenXmlPart).FirstOrDefault() is { } part) parts[a.Value!] = part;
                else if (sp.HyperlinkRelationships.FirstOrDefault(h => h.Id == a.Value) is { } h) links[a.Value!] = (h.Uri, h.IsExternal);
            }
        }
        return new Clip(this, sp, list, parts, links);
    }

    /// <summary>
    /// Adds copies of the clip's shapes to <paramref name="target"/>, moved by (<paramref name="dx"/>, <paramref name="dy"/>),
    /// with new shape ids and relationships of the target slide. Shapes whose parts are gone are left out. Returns the new shapes.
    /// </summary>
    public List<OpenXmlElement> Paste(SlidePart target, Clip clip, double dx = 0, double dy = 0)
    {
        var added = new List<OpenXmlElement>();
        if (clip.Owner != this) return added;
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
            uint next = NextId(target);
            foreach (var nv in el.Descendants().Where(e => e.LocalName == "cNvPr")) SetAttr(nv, "id", (next++).ToString());
            if ((dx != 0 || dy != 0) && X.Child(XfrmOf(el), "off") is { } off)
            {
                SetAttr(off, "x", ((X.Long(off, "x") ?? 0) + (long)(dx * SlideRenderer.Emu)).ToString());
                SetAttr(off, "y", ((X.Long(off, "y") ?? 0) + (long)(dy * SlideRenderer.Emu)).ToString());
            }
            AppendShape(target, el);
            added.Add(el);
        }
        if (added.Count > 0) Dirty = true;
        return added;
    }

    // ---------- format painter ----------

    /// <summary>What the format painter carries: fill, outline, the shape style, and the first run's character formatting.</summary>
    public sealed record Format(OpenXmlElement? Fill, OpenXmlElement? Line, OpenXmlElement? Style, OpenXmlElement? Run, string Kind);

    public static Format TakeFormat(OpenXmlElement el) => new(
        Take(SpPr(el), SpPrOrder, "solidFill"), X.Child(SpPr(el), "ln")?.CloneNode(true), X.Child(el, "style")?.CloneNode(true),
        TextBodies(el).SelectMany(t => t.Descendants()).FirstOrDefault(e => e.LocalName is "rPr" or "endParaRPr")?.CloneNode(true), el.LocalName);

    static readonly string[] RunAttrs = ["b", "i", "u", "strike", "sz", "baseline", "spc", "cap"];

    /// <summary>Gives <paramref name="el"/> the format <see cref="TakeFormat"/> took: it then looks the same.</summary>
    public static void ApplyFormat(OpenXmlElement el, Format f)
    {
        if (CanFormatShape(el) && f.Kind is "sp" or "pic" or "cxnSp" && SpPr(el) is { } spPr)
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
            EditRuns(el, to =>
            {
                foreach (var a in RunAttrs) SetAttr(to, a, X.Attr(from, a));
                foreach (var g in new[] { "solidFill", "highlight", "latin", "ea" }) Put(to, RPrOrder, g, Take(from, RPrOrder, g));
            });
    }
}
