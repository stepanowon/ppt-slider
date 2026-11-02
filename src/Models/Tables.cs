using System.Windows;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using static PptSlider.Ooxml;
using static PptSlider.SlideTree;

namespace PptSlider;

/// <summary>Tables (a:tbl in a graphicFrame): cells, inserting and deleting rows and columns with their merges, size.</summary>
static class Tables
{
    public static bool IsTable(OpenXmlElement? el) => el?.LocalName == "graphicFrame" && Tbl(el) != null;
    static OpenXmlElement? Tbl(OpenXmlElement frame) => frame.Descendants().FirstOrDefault(e => e.LocalName == "tbl");

    static List<OpenXmlElement> Rows(OpenXmlElement tbl) => tbl.ChildElements.Where(e => e.LocalName == "tr").ToList();
    static List<OpenXmlElement> Cells(OpenXmlElement tr) => tr.ChildElements.Where(e => e.LocalName == "tc").ToList();
    static List<OpenXmlElement> GridColumns(OpenXmlElement tbl) => X.Child(tbl, "tblGrid")?.ChildElements.Where(e => e.LocalName == "gridCol").ToList() ?? [];

    /// <summary>The cells of a table frame, row by row, without the ones a merge covers.</summary>
    public static List<OpenXmlElement> TableCells(OpenXmlElement frame) =>
        Tbl(frame) is { } tbl ? Rows(tbl).SelectMany(Cells).Where(c => X.Attr(c, "hMerge") is not ("1" or "true") && X.Attr(c, "vMerge") is not ("1" or "true")).ToList() : [];

    public static (int Row, int Col) CellIndex(OpenXmlElement tc) => (Rows(tc.Parent!.Parent!).IndexOf(tc.Parent!), Cells(tc.Parent!).IndexOf(tc));

    public static OpenXmlElement Cell(OpenXmlElement frame, int row, int col) => Cells(Rows(Tbl(frame)!)[row])[col];

    public static (int Rows, int Cols) TableSize(OpenXmlElement frame) =>
        Tbl(frame) is { } tbl ? (Rows(tbl).Count, GridColumns(tbl).Count) : (0, 0);

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
            <p:xfrm><a:off x="{{Emu(r.X)}}" y="{{Emu(r.Y)}}"/><a:ext cx="{{w * cols}}" cy="{{h * rows}}"/></p:xfrm>
            <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/table"><a:tbl><a:tblPr firstRow="1" bandRow="1"><a:tableStyleId>{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}</a:tableStyleId></a:tblPr>
            <a:tblGrid>{{grid}}</a:tblGrid>{{trs}}</a:tbl></a:graphicData></a:graphic></p:graphicFrame>
            """);
        AppendShape(sp, frame);
        return frame;
    }

    /// <summary>A table is drawn from its column widths and row heights: a new frame size (New / Old EMU) scales them.</summary>
    public static void Scale(OpenXmlElement frame, (long New, long Old) width, (long New, long Old) height)
    {
        if (Tbl(frame) is not { } tbl) return;
        foreach (var col in GridColumns(tbl))
            SetAttr(col, "w", ((long)Math.Round((X.Long(col, "w") ?? 0) * (double)width.New / width.Old)).ToString());
        foreach (var tr in Rows(tbl))
            SetAttr(tr, "h", ((long)Math.Round((X.Long(tr, "h") ?? 0) * (double)height.New / height.Old)).ToString());
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

    static bool Merged(OpenXmlElement tc, string attr) => X.Attr(tc, attr) is "1" or "true";

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
        var gcols = GridColumns(tbl);
        var gc = gcols[col].CloneNode(true);
        foreach (var e in gc.ChildElements.Where(e => e.LocalName == "extLst").ToList()) e.Remove();
        if (right) gcols[col].InsertAfterSelf(gc); else gcols[col].InsertBeforeSelf(gc);
        int k = right ? col + 1 : col;
        foreach (var tr in Rows(tbl))
        {
            var cells = Cells(tr);
            var tc = Blank(cells[col]);
            // inside a horizontal merge: the merge takes the new column in
            if (k < cells.Count && Merged(cells[k], "hMerge"))
            {
                int o = k;
                while (o > 0 && Merged(cells[o], "hMerge")) o--;
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
                TakeText(tc, next);
            }
            else if (Merged(tc, "vMerge"))
                for (int r = row - 1; r >= 0; r--)
                    if (Cells(rows[r]).ElementAtOrDefault(c) is { } up && !Merged(up, "vMerge"))
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
        var gcols = GridColumns(tbl);
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
                TakeText(tc, next);
            }
            else if (Merged(tc, "hMerge"))
                for (int o = col - 1; o >= 0; o--)
                    if (!Merged(cells[o], "hMerge"))
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

    /// <summary>A merge's next cell takes over the text of the cell going away.</summary>
    static void TakeText(OpenXmlElement from, OpenXmlElement to)
    {
        if (X.Child(from, "txBody") is { } tx && X.Child(to, "txBody") is { } old) { old.InsertBeforeSelf(tx.CloneNode(true)); old.Remove(); }
    }
}
