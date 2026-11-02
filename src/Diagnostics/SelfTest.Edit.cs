using System.IO;
using System.Windows;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace PptSlider;

static partial class SelfTest
{
    /// <summary>
    /// PowerPoint-style editing on a new slide: shapes drawn from the gallery, fill / outline / rotation, arranging, multiple
    /// selection, clipboard, paragraph and character formatting (in the shape and in the editor), tables, format painter,
    /// redo. The slide must come out as valid OOXML.
    /// </summary>
    static void EditingTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, Width = 1400, Height = 860, ShowActivated = false };
        try
        {
            main.Show();
            main.Open(file);
            main.Run("newSlide"); // everything below goes on this empty slide
            Wait(100);
            var deck = main.Deck!;
            var sp = main.CurrentSlide!;
            int slides = main.Count;
            List<OpenXmlElement> Shapes() => sp.Slide!.CommonSlideData!.ShapeTree!.ChildElements.Where(e => e.LocalName is "sp" or "pic" or "cxnSp" or "graphicFrame" or "grpSp").ToList();
            Rect Bounds(OpenXmlElement el) { var h = new List<ShapeHit>(); deck.Renderer.Render(sp, h); return h.First(x => x.Element == el).Bounds; }
            string? Fill(OpenXmlElement el) => X.Attr(X.Child(X.Child(X.Child(el, "spPr"), "solidFill"), "srgbClr"), "val");

            // ---- shapes from the gallery, drawn by dragging ----
            var drawn = new List<OpenXmlElement>();
            foreach (var (kind, i) in ShapeFactory.ShapeKinds.Select((k, i) => (k, i)))
            {
                double x = 20 + i % 10 * 62, y = 300 + i / 10 * 80; // two rows: the test deck is 4:3
                main.StageInput.StartShape(kind);
                main.StageInput.DrawPendingShape(new Point(x + 50, y), new Point(x, y + 60)); // right to left, top to bottom
                drawn.Add(main.Selection.Primary!);
            }
            check(drawn.Distinct().Count() == ShapeFactory.ShapeKinds.Length && Shapes().Count == drawn.Count, $"shapes: all {drawn.Count} kinds drawn");
            var arrow = drawn[^1];
            check(arrow.LocalName == "cxnSp" && X.Attr(X.Child(X.Child(arrow, "spPr"), "xfrm"), "flipH") == "1" && arrow.Descendants().Any(e => e.LocalName == "tailEnd"),
                "shapes: an arrow drawn right to left is a flipped connector with an arrowhead");
            var rect = drawn[0];
            check(Math.Abs(Bounds(rect).Width - 50) < 1 && Math.Abs(Bounds(rect).Height - 60) < 1 && Math.Abs(Bounds(rect).X - 20) < 1, $"shapes: a rectangle fills the dragged box ({Bounds(rect)})");
            main.StageInput.StartShape("ellipse");
            main.StageInput.DrawPendingShape(new Point(800, 40), new Point(800, 40)); // a plain click
            check(Math.Abs(Bounds(main.Selection.Primary!).Width - 96) < 1, "shapes: a click draws a one-inch shape");
            Png(deck.Renderer.Render(sp), Path.Combine(outDir, "edit-shapes.png"));

            // ---- fill, outline, rotation ----
            main.ShapeCommands.Select(rect);
            main.ShapeCommands.SetFill(Colors.Red);
            main.ShapeCommands.SetOutline(null);
            var spPr = X.Child(rect, "spPr")!;
            check(Fill(rect) == "FF0000" && X.Child(X.Child(spPr, "ln"), "noFill") != null, "format: red fill, no outline");
            main.ShapeCommands.SetOutlineWidth(3);
            check(X.Attr(X.Child(spPr, "ln"), "w") == "38100" && X.Child(X.Child(spPr, "ln"), "solidFill") != null, "format: an outline weight turns the outline back on");
            main.ShapeCommands.Rotate(90);
            main.ShapeCommands.Flip(true);
            check(X.Attr(X.Child(spPr, "xfrm"), "rot") == "5400000" && X.Attr(X.Child(spPr, "xfrm"), "flipH") == "1", "format: rotated 90° and flipped");

            // ---- drawing order, several selected, align ----
            var (a, b) = (drawn[1], drawn[2]);
            main.ShapeCommands.Select(a);
            main.Run("bringToFront");
            check(Shapes()[^1] == a, "arrange: bring to front (Ctrl+Shift+])");
            main.Run("sendToBack");
            check(Shapes()[0] == a, "arrange: send to back (Ctrl+Shift+[)");
            main.Run("bringForward");
            check(Shapes()[1] == a, "arrange: one step forward (Ctrl+])");
            main.ShapeCommands.Select(a, b);
            var (ra, rb) = (Bounds(a), Bounds(b));
            main.ShapeCommands.Nudge(8, 0);
            check(Math.Abs(Bounds(a).X - ra.X - 8) < 0.5 && Math.Abs(Bounds(b).X - rb.X - 8) < 0.5 && main.Selection.Count == 2, "select: an arrow key moves every selected shape");
            main.ShapeCommands.Align("left");
            check(Math.Abs(Bounds(a).X - Bounds(b).X) < 0.5, "align: left edges line up");

            // ---- clipboard ----
            main.ShapeCommands.Select(rect);
            int n = Shapes().Count;
            main.Run("copy");
            main.Run("paste");
            var pasted = main.Selection.Primary!;
            var ids = sp.Slide!.Descendants().Where(e => e.LocalName == "cNvPr").Select(e => X.Attr(e, "id")).ToList();
            check(Shapes().Count == n + 1 && pasted != rect && ids.Distinct().Count() == ids.Count && Fill(pasted) == "FF0000", "clipboard: Ctrl+C, Ctrl+V adds a copy with its own id");
            check(((Bounds(pasted).TopLeft - Bounds(rect).TopLeft) - new Vector(10, 10)).Length < 0.5, "clipboard: pasted on its own slide a little lower right");
            main.Run("duplicate");
            check(Shapes().Count == n + 2 && main.Count == slides, "clipboard: Ctrl+D with a shape selected copies the shape, not the slide");
            main.Run("cut");
            check(Shapes().Count == n + 1, "clipboard: Ctrl+X takes it out");
            main.Run("paste");
            check(Shapes().Count == n + 2, "clipboard: Ctrl+V puts it back");
            main.InsertCommands.Picture(TinyPng(), ShapeFactory.PictureType(".png")!.Value);
            main.Run("copy");
            int here = main.Current;
            main.Go(0);
            main.Run("paste");
            var embed = main.Selection.Primary?.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;
            bool hasImage;
            try { hasImage = embed != null && main.CurrentSlide!.GetPartById(embed) is ImagePart; } catch (ArgumentOutOfRangeException) { hasImage = false; }
            check(hasImage, "clipboard: a picture pasted on another slide brings its image");
            main.Go(here);

            // ---- paragraphs: the whole shape ----
            deck.History.Snapshot(sp);
            var tb = ShapeFactory.AddTextBox(sp, new Rect(100, 60, 500, 120), "");
            TextContent.SetText(tb, "첫째\n둘째");
            main.Go(here); // draw it
            main.ShapeCommands.Select(tb);
            A.ParagraphProperties? PPr(int i) => TextFormat.Paragraphs(tb)[i].ParagraphProperties;
            main.TextCommands.ToggleBullets("buChar");
            check(PPr(0)?.GetFirstChild<A.CharacterBullet>() != null && PPr(1)?.GetFirstChild<A.CharacterBullet>() != null && X.Attr(PPr(0), "indent") == "-342900",
                "paragraph: bullets on, with a hanging indent");
            main.TextCommands.ToggleBullets("buChar");
            check(PPr(0)?.GetFirstChild<A.NoBullet>() != null && PPr(0)?.GetFirstChild<A.CharacterBullet>() == null, "paragraph: bullets off again");
            main.TextCommands.ToggleBullets("buAutoNum");
            check(PPr(1)?.GetFirstChild<A.AutoNumberedBullet>() != null, "paragraph: numbering");
            main.TextCommands.Indent(1);
            check(X.Attr(PPr(0), "lvl") == "1", "paragraph: list level in");
            main.TextCommands.SetLineSpacing(1.5);
            main.TextCommands.SetAlign("just");
            check(PPr(0)?.LineSpacing?.GetFirstChild<A.SpacingPercent>()?.Val?.Value == 150000 && X.Attr(PPr(1), "algn") == "just", "paragraph: line spacing 1.5 and justified");
            main.Run("strike");
            main.TextCommands.SetHighlight(Colors.Yellow);
            var rPrs = tb.Descendants<A.RunProperties>().ToList();
            check(rPrs.Count > 0 && rPrs.All(r => X.Attr(r, "strike") == "sngStrike" && X.Attr(X.Child(X.Child(r, "highlight"), "srgbClr"), "val") == "FFFF00"),
                "characters: strikethrough and highlight on the whole shape");

            // ---- the editor: a paragraph setting on the selected paragraph only, formatting on selected characters ----
            deck.History.Snapshot(sp);
            var tb2 = ShapeFactory.AddTextBox(sp, new Rect(100, 520, 500, 160), "");
            TextContent.SetText(tb2, "가나\n다라\n마바");
            main.Go(here);
            main.ShapeCommands.Select(tb2);
            main.Run("editText");
            var ed = main.TextEditor.Box!;
            var blocks = ed.Document.Blocks.ToList();
            ed.Selection.Select(blocks[1].ContentStart, blocks[1].ContentEnd);
            main.TextCommands.ToggleBullets("buChar");
            var ps2 = TextFormat.Paragraphs(tb2);
            check(ps2[1].ParagraphProperties?.GetFirstChild<A.CharacterBullet>() != null && ps2[0].ParagraphProperties?.GetFirstChild<A.CharacterBullet>() == null
                  && ps2[2].ParagraphProperties?.GetFirstChild<A.CharacterBullet>() == null && main.TextEditor.Box != null,
                $"editor: bullets on the selected paragraph only, still editing ({string.Join(",", ps2.Select(p => p.ParagraphProperties?.GetFirstChild<A.CharacterBullet>() != null ? 1 : 0))}, editing {main.TextEditor.Box != null})");
            if (main.TextEditor.Box == null) return;
            ed = main.TextEditor.Box!;
            var first = (System.Windows.Documents.Paragraph)ed.Document.Blocks.First();
            ed.Selection.Select(first.ContentStart.GetPositionAtOffset(1)!, first.ContentStart.GetPositionAtOffset(2)!); // "가"
            main.Run("strike");
            main.TextCommands.SetColor(Colors.Red);
            main.TextCommands.SetHighlight(Colors.Yellow);
            main.TextEditor.Commit();
            var runs = TextFormat.Paragraphs(tb2)[0].Elements<A.Run>().ToList();
            var ga = runs.FirstOrDefault(r => r.Text?.Text == "가")?.RunProperties;
            var na = runs.FirstOrDefault(r => r.Text?.Text == "나")?.RunProperties;
            check(X.Attr(ga, "strike") == "sngStrike" && X.Attr(X.Child(X.Child(ga, "solidFill"), "srgbClr"), "val") == "FF0000"
                  && X.Attr(X.Child(X.Child(ga, "highlight"), "srgbClr"), "val") == "FFFF00" && X.Attr(na, "strike") != "sngStrike" && X.Child(na, "highlight") == null,
                $"editor: strikethrough, color and highlight on one character ({string.Join("|", runs.Select(r => r.Text?.Text))})");

            // ---- tables ----
            main.InsertCommands.Table(3, 4);
            var frame = main.Selection.Primary!;
            check(Tables.IsTable(frame) && Tables.TableSize(frame) == (3, 4), "table: 3 x 4 inserted");
            var c00 = Tables.Cell(frame, 0, 0);
            check(main.TextEditor.EditCell(c00) && main.TextEditor.Box != null, "table: a cell opens in the editor");
            main.TextEditor.Box!.Selection.Text = "A1";
            main.TextEditor.NextCell(c00, 1);
            check(TextContent.GetText(c00) == "A1" && main.Selection.Cell == Tables.Cell(frame, 0, 1) && main.TextEditor.Box != null, "table: typed in a cell, Tab goes to the next");
            main.TextEditor.Box!.Selection.Text = "B1";
            main.TextEditor.Commit();
            var last = Tables.Cell(frame, 2, 3);
            main.TextEditor.EditCell(last);
            main.TextEditor.NextCell(last, 1);
            check(Tables.TableSize(frame) == (4, 4) && main.Selection.Cell == Tables.Cell(frame, 3, 0), "table: Tab in the last cell adds a row");
            main.TextEditor.Commit();
            main.TextEditor.EditCell(Tables.Cell(frame, 0, 1));
            main.TextEditor.Commit(); // the current cell: row 1, column 2
            main.TableCommands.Apply((f, _, _, c) => Tables.InsertColumn(f, c, true));
            var tbl = frame.Descendants<A.Table>().First();
            check(Tables.TableSize(frame) == (4, 5) && tbl.Elements<A.TableRow>().All(r => r.Elements<A.TableCell>().Count() == 5) && TextContent.GetText(Tables.Cell(frame, 0, 1)) == "B1"
                  && TextContent.GetText(Tables.Cell(frame, 0, 2)) == "", "table: a column inserted right of the current cell, empty");
            main.TableCommands.Apply((f, _, _, c) => Tables.DeleteColumn(f, c));
            main.TableCommands.Apply((f, _, r, _) => Tables.DeleteRow(f, r));
            check(Tables.TableSize(frame) == (3, 4) && TextContent.GetText(Tables.Cell(frame, 0, 0)) == "", "table: deleting the current cell's column and row");
            Ooxml.SetAttr(Tables.Cell(frame, 0, 0), "gridSpan", "2");
            Ooxml.SetAttr(Tables.Cell(frame, 0, 1), "hMerge", "1");
            Tables.InsertColumn(frame, 0, true);
            check(X.Attr(Tables.Cell(frame, 0, 0), "gridSpan") == "3" && X.Attr(Tables.Cell(frame, 0, 1), "hMerge") == "1", "table: a column inserted inside a merge widens it");
            Tables.DeleteColumn(frame, 0);
            check(X.Attr(Tables.Cell(frame, 0, 0), "gridSpan") == "2" && X.Attr(Tables.Cell(frame, 0, 0), "hMerge") == null && Tables.TableSize(frame) == (3, 4),
                "table: deleting a merge's first column hands the merge on");
            long Width() => frame.Descendants<A.GridColumn>().Sum(g => g.Width?.Value ?? 0);
            long w0 = Width();
            var fb = Bounds(frame);
            ShapeLayout.SetBounds(frame, new Rect(fb.X, fb.Y, fb.Width * 2, fb.Height));
            check(Math.Abs(Width() - 2 * w0) < 10, "table: resizing scales the columns");
            ShapeLayout.SetBounds(frame, fb);
            main.Go(here);

            // ---- format painter, copy / paste formatting ----
            var (src, dst, dst2) = (drawn[3], drawn[4], drawn[5]);
            main.ShapeCommands.Select(src);
            main.ShapeCommands.SetFill(Color.FromRgb(0x00, 0x80, 0x00));
            main.OnFormatPainter(null, null);
            main.FormatPainterTool.PaintOn(dst);
            check(Fill(dst) == "008000" && main.Selection.Primary == dst, "format painter: the next shape clicked gets the format");
            main.ShapeCommands.Select(src);
            main.Run("copyFormat");
            main.ShapeCommands.Select(dst2);
            main.Run("pasteFormat");
            check(Fill(dst2) == "008000", "format: Ctrl+Shift+C, Ctrl+Shift+V");

            // ---- the file ----
            Png(deck.Renderer.Render(sp), Path.Combine(outDir, "edit-all.png"));
            var saved = Path.Combine(outDir, "editing.pptx");
            deck.SaveCopy(saved);
            using (var doc = PresentationDocument.Open(saved, false))
            {
                var pres = doc.PresentationPart!;
                var part = (SlidePart)pres.GetPartById(pres.Presentation!.SlideIdList!.Elements<P.SlideId>().ElementAt(here).RelationshipId!);
                var errs = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).Where(e => e.Part?.Uri == part.Uri).ToList();
                check(errs.Count == 0, $"file: the edited slide is valid OOXML ({errs.Count} errors{(errs.Count > 0 ? ": " + string.Join(" | ", errs.Take(4).Select(e => e.Description + " @ " + e.Path?.XPath)) : "")})");
            }
            using (var re = Deck.Open(saved))
            {
                var hs = new List<ShapeHit>();
                Png(re.Renderer.Render(re.Slides[here], hs), Path.Combine(outDir, "edit-reopened.png"));
                check(hs.Count == Shapes().Count, $"file: reopened, all {hs.Count} shapes are drawn");
            }

            // ---- redo (last: undo replaces the slide's objects) ----
            n = Shapes().Count;
            main.StageInput.StartShape("rect");
            main.StageInput.DrawPendingShape(new Point(10, 10), new Point(60, 60));
            main.Run("undo");
            check(Shapes().Count == n, "redo: Ctrl+Z takes the shape out");
            main.Run("redo");
            check(Shapes().Count == n + 1, "redo: Ctrl+Y puts it back");
            main.Run("undo");
            main.StageInput.StartShape("ellipse");
            main.StageInput.DrawPendingShape(new Point(10, 10), new Point(60, 60));
            main.Run("redo");
            check(Shapes().Count == n + 1, "redo: a new edit after undo leaves nothing to redo");
        }
        finally { main.CloseForTest(); }
    }
}
