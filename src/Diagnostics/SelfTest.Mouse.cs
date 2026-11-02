using System.Windows;
using System.Windows.Input;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;

namespace PptSlider;

static partial class SelfTest
{
    /// <summary>
    /// Editing the way a user does it: presses, drags and double-clicks on the slide (in slide units) and key presses, through
    /// the same code the mouse and key handlers use. Also checks that no two commands share a default key.
    /// </summary>
    static void MouseKeyTests(string file, string outDir, Action<bool, string> check)
    {
        // ---- default keys: one command per key ----
        var dup = Shortcuts.All.SelectMany(a => a.Defaults.Select(g => (g, a.Id))).GroupBy(x => x.g).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(",", g.Select(x => x.Id))}").ToList();
        check(dup.Count == 0, $"keys: no default key is used twice ({string.Join(" | ", dup)})");

        var main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, Width = 1400, Height = 860, ShowActivated = false };
        try
        {
            main.SyntheticInput = true;
            main.Show();
            main.Open(file);
            main.Run("newSlide");
            Wait(100);
            var deck = main.Deck!;
            var sp = main.CurrentSlide!;
            List<OpenXmlElement> Shapes() => sp.Slide!.CommonSlideData!.ShapeTree!.ChildElements.Where(e => e.LocalName is "sp" or "pic" or "cxnSp" or "graphicFrame").ToList();
            Rect B(OpenXmlElement el) { var h = new List<ShapeHit>(); deck.Renderer.Render(sp, h); return h.First(x => x.Element == el).Bounds; }
            const ModifierKeys None = ModifierKeys.None, Shift = ModifierKeys.Shift, Ctrl = ModifierKeys.Control;
            void Drag(Point a, Point b, ModifierKeys m = None) { main.StageDown(a, m, 1); main.StageMove(a + (b - a) / 2, m); main.StageMove(b, m); main.StageUp(); }
            void Click(Point p, ModifierKeys m = None) { main.StageDown(p, m, 1); main.StageUp(); }
            bool Key(Key k, ModifierKeys m = None) => main.HandleKey(new Gesture(k, m));
            static Point Mid(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
            static bool Near(Rect a, Rect b, double t = 0.6) => Math.Abs(a.X - b.X) < t && Math.Abs(a.Y - b.Y) < t && Math.Abs(a.Width - b.Width) < t && Math.Abs(a.Height - b.Height) < t;

            // ---- drawing shapes ----
            main.StartShape("rect");
            Drag(new Point(40, 40), new Point(160, 100));
            var r1 = main.SelectedElement!;
            check(Shapes().Count == 1 && Near(B(r1), new Rect(40, 40, 120, 60)), $"mouse: a rectangle drawn by dragging ({B(r1)})");
            main.StartShape("ellipse");
            Drag(new Point(300, 40), new Point(380, 70), Shift);
            var e1 = main.SelectedElement!;
            check(Near(B(e1), new Rect(300, 40, 80, 80)), $"mouse: Shift draws a circle ({B(e1)})");
            main.StartShape("rect");
            check(Key(System.Windows.Input.Key.Escape), "keys: Esc cancels the picked shape");
            Drag(new Point(500, 300), new Point(520, 320));
            check(Shapes().Count == 2, "mouse: after Esc a drag draws nothing");
            main.StartShape("line");
            Drag(new Point(40, 200), new Point(240, 200));
            var line = main.SelectedElement!;
            check(line.LocalName == "cxnSp" && Math.Abs(B(line).Width - 200) < 0.6 && B(line).Height < 0.6, $"mouse: a straight line ({B(line)})");

            // ---- selecting ----
            Click(new Point(600, 500));
            check(main.Selection.Count == 0, "mouse: a click on empty space clears the selection");
            Click(Mid(B(r1)));
            check(main.Selection is [var s1] && s1 == r1, "mouse: a click selects a shape");
            Click(Mid(B(e1)), Shift);
            check(main.Selection.Count == 2, "mouse: Shift+click adds a shape");
            Click(Mid(B(e1)), Ctrl);
            check(main.Selection is [var s2] && s2 == r1, "mouse: Ctrl+click on a selected shape takes it out");
            Click(new Point(240, 200 + 3)); // 3 px off a thin line still picks it
            check(main.SelectedElement == line, "mouse: a thin line can be clicked");
            Drag(new Point(20, 20), new Point(420, 150));
            check(main.Selection.Count == 2 && main.Selection.Contains(r1) && main.Selection.Contains(e1), $"mouse: a box dragged on empty space selects what it covers ({main.Selection.Count})");
            Click(Mid(B(e1)));
            check(main.Selection is [var s3] && s3 == e1, "mouse: a click on one of several selected selects just it");

            // ---- moving ----
            main.SelectShapes(r1, e1);
            var (b1, b2) = (B(r1), B(e1));
            Drag(Mid(b1), Mid(b1) + new Vector(30, 20));
            check(Near(B(r1), Rect.Offset(b1, 30, 20)) && Near(B(e1), Rect.Offset(b2, 30, 20)) && main.Selection.Count == 2, "mouse: dragging one of several moves them all");
            check(Key(System.Windows.Input.Key.Z, Ctrl) && Near(B(Shapes()[0]), b1), "keys: Ctrl+Z puts them back");
            r1 = Shapes()[0]; e1 = Shapes()[1]; line = Shapes()[2]; // undo replaced the slide's objects
            check(main.Selection.Count == 0, "keys: nothing stays selected after undo");
            check(Key(System.Windows.Input.Key.Y, Ctrl) && Near(B(Shapes()[0]), Rect.Offset(b1, 30, 20)), "keys: Ctrl+Y moves them again");
            r1 = Shapes()[0]; e1 = Shapes()[1]; line = Shapes()[2];
            b1 = B(r1);

            // ---- resizing and rotating with the handles ----
            main.SelectShapes(r1);
            Drag(b1.BottomRight, b1.BottomRight + new Vector(40, 20));
            check(Near(B(r1), new Rect(b1.X, b1.Y, b1.Width + 40, b1.Height + 20)), $"mouse: the bottom-right handle resizes ({B(r1)})");
            b1 = B(r1);
            Drag(b1.TopLeft, b1.TopLeft + new Vector(10, 10));
            check(Near(B(r1), new Rect(b1.X + 10, b1.Y + 10, b1.Width - 10, b1.Height - 10)), "mouse: the top-left handle moves that corner");
            b1 = B(r1);
            Drag(new Point(b1.Right, b1.Y + b1.Height / 2), new Point(b1.Right + 25, b1.Y));
            check(Near(B(r1), new Rect(b1.X, b1.Y, b1.Width + 25, b1.Height)), "mouse: a side handle changes only the width");
            b1 = B(r1);
            Drag(new Point(b1.Right, b1.Bottom), new Point(b1.Left - 100, b1.Bottom));
            check(B(r1).Width >= 7.9, $"mouse: a shape can't be squeezed to nothing ({B(r1).Width:0.#})");
            main.Run("undo");
            r1 = Shapes()[0]; e1 = Shapes()[1]; line = Shapes()[2];
            main.SelectShapes(r1); b1 = B(r1);
            var top = new Point(b1.X + b1.Width / 2, b1.Y - 22 * main.PxForTest); // the rotate handle, 22 screen px above
            Drag(top, new Point(b1.Right + 200, b1.Y + b1.Height / 2 + 3), Shift);
            check(Deck.Rotation(r1) == 90, $"mouse: Shift-dragging the rotate handle turns in 15° steps ({Deck.Rotation(r1)})");
            Drag(Mid(b1), Mid(b1) + new Vector(0, 50));
            check(Deck.Rotation(r1) == 90 && Near(B(r1), Rect.Offset(b1, 0, 50)), "mouse: a rotated shape moves and stays rotated");
            b1 = B(r1);
            var bottom = new Point(b1.X + b1.Width / 2, b1.Bottom); // the bottom handle, turned 90°: now at the left
            var turned = new System.Windows.Media.RotateTransform(90, Mid(b1).X, Mid(b1).Y).Transform(bottom);
            Drag(turned, turned + new Vector(-20, 0));
            check(Math.Abs(B(r1).Height - (b1.Height + 20)) < 0.6 && Deck.Rotation(r1) == 90, $"mouse: a rotated shape's handle pulls along the turned side ({B(r1)} from {b1})");

            // ---- pictures keep their shape ----
            main.InsertPicture(TinyPng(), Deck.PictureType(".png")!.Value);
            var pic = main.SelectedElement!;
            var pb = B(pic);
            Drag(pb.BottomRight, pb.BottomRight + new Vector(pb.Width, 0));
            check(Math.Abs(B(pic).Width / B(pic).Height - pb.Width / pb.Height) < 0.02, $"mouse: a picture's corner keeps its proportions ({B(pic)})");
            pb = B(pic);
            Drag(pb.BottomRight, pb.BottomRight + new Vector(30, 0), Shift);
            check(Math.Abs(B(pic).Width - pb.Width - 30) < 0.6 && Math.Abs(B(pic).Height - pb.Height) < 0.6, "mouse: Shift frees a picture's proportions");
            check(Key(System.Windows.Input.Key.Delete) && !Shapes().Contains(pic), "keys: Delete removes the selected shape");

            // ---- keys ----
            main.SelectShapes(e1);
            var eb = B(e1);
            Key(System.Windows.Input.Key.Right);
            Key(System.Windows.Input.Key.Down, Ctrl);
            check(Near(B(e1), Rect.Offset(eb, 8, 1)), "keys: arrows move the shape (Ctrl: 1 px)");
            int at = main.Current;
            Key(System.Windows.Input.Key.Escape);
            check(main.Selection.Count == 0, "keys: Esc clears the selection");
            Key(System.Windows.Input.Key.Right);
            check(main.Current != at || main.Count == at + 1, "keys: with nothing selected the arrows go to the next slide");
            main.Go(at);
            Key(System.Windows.Input.Key.A, Ctrl);
            check(main.Selection.Count == Shapes().Count, "keys: Ctrl+A selects every shape");
            main.SelectShapes(r1);
            Key(System.Windows.Input.Key.OemOpenBrackets, Ctrl | Shift);
            check(Shapes()[0] == r1, "keys: Ctrl+Shift+[ sends to back");
            Key(System.Windows.Input.Key.OemCloseBrackets, Ctrl | Shift);
            check(Shapes()[^1] == r1, "keys: Ctrl+Shift+] brings to front");
            int n = Shapes().Count;
            Key(System.Windows.Input.Key.C, Ctrl);
            Key(System.Windows.Input.Key.V, Ctrl);
            Key(System.Windows.Input.Key.V, Ctrl);
            check(Shapes().Count == n + 2 && Near(B(main.SelectedElement!), Rect.Offset(B(r1), 20, 20)), "keys: Ctrl+C, Ctrl+V twice: each copy a little further");
            Key(System.Windows.Input.Key.D, Ctrl);
            check(Shapes().Count == n + 3, "keys: Ctrl+D duplicates the shape");

            // ---- text: double-click, editor keys ----
            deck.Snapshot(sp);
            var tb = Deck.AddTextBox(sp, new Rect(400, 300, 300, 100), "");
            Deck.SetText(tb, "하나 둘\n셋");
            main.Go(at);
            main.StageDown(Mid(B(tb)), None, 1); main.StageUp();
            main.StageDown(Mid(B(tb)), None, 2);
            check(main.Editor != null && main.SelectedElement == tb, "mouse: a double-click on text starts editing");
            if (main.Editor is { } ed)
            {
                ed.SelectAll();
                Key(System.Windows.Input.Key.B, Ctrl);
                Key(System.Windows.Input.Key.Right, ModifierKeys.Alt | Shift);
                check(main.Editor != null, "keys: Ctrl+B and Alt+Shift+→ keep the editor open");
                main.Editor!.SelectAll();
                Key(System.Windows.Input.Key.Escape);
                check(main.Editor == null, "keys: Esc leaves the editor");
            }
            tb = Shapes().First(e => Deck.GetText(e).StartsWith("하나"));
            var ps = Deck.Paragraphs(tb);
            check(ps.All(p => X.Attr(p.ParagraphProperties, "lvl") == "1") && tb.Descendants<A.RunProperties>().Any(r => r.Bold?.Value == true),
                $"keys: bold and list level kept after the editor closes ({string.Join(",", ps.Select(p => X.Attr(p.ParagraphProperties, "lvl")))})");
            main.SelectShapes(tb);
            Click(Mid(B(tb)));
            Key(System.Windows.Input.Key.F2);
            check(main.Editor != null, "keys: F2 edits the selected text");
            Click(new Point(3, 3));
            check(main.Editor == null, "mouse: a click outside closes the editor");

            // ---- tables: double-click a cell, Tab / Shift+Tab ----
            main.InsertTable(2, 3);
            var frame = main.SelectedElement!;
            var fb = B(frame);
            var p12 = new Point(fb.X + fb.Width * 5 / 6, fb.Y + fb.Height * 3 / 4); // row 2, column 3
            main.StageDown(p12, None, 1); main.StageUp();
            check(main.CurrentCell == Deck.Cell(frame, 1, 2), "mouse: a click in a table picks the cell under it");
            main.StageDown(p12, None, 2);
            check(main.Editor != null && main.CurrentCell == Deck.Cell(frame, 1, 2), "mouse: a double-click edits that cell");
            Key(System.Windows.Input.Key.Tab, Shift);
            check(main.CurrentCell == Deck.Cell(frame, 1, 1), "keys: Shift+Tab goes back a cell");
            main.Editor!.Selection.Text = "x";
            Key(System.Windows.Input.Key.Tab);
            Key(System.Windows.Input.Key.Tab);
            check(Deck.TableSize(frame) == (3, 3) && Deck.GetText(Deck.Cell(frame, 1, 1)) == "x", "keys: Tab past the last cell adds a row");
            Key(System.Windows.Input.Key.Escape);

            // ---- format painter by clicking ----
            main.SelectShapes(e1);
            main.SetShapeFill(System.Windows.Media.Colors.Purple);
            main.OnFormatPainter(null, null);
            Click(new Point(B(line).X + 5, B(line).Y + 2)); // the line's left end: the turned rectangle covers its middle by now
            check(main.SelectedElement == line, "painter: the shape clicked is selected");
            string? Fill(OpenXmlElement? el) => X.Attr(X.Child(X.Child(X.Child(el, "spPr"), "solidFill"), "srgbClr"), "val");
            Click(Mid(B(r1))); // what is on top there (copies of r1 by now)
            check(Fill(main.SelectedElement) != "800080", "painter: one use only (the second click selects)");
            main.SelectShapes(e1);
            main.OnFormatPainter(null, null);
            Click(new Point(3, 3));
            Click(Mid(B(r1)));
            check(main.SelectedElement != e1 && Fill(main.SelectedElement) != "800080", "painter: a click on empty space puts it away");
            main.SelectShapes(e1);
            main.OnFormatPainter(null, null);
            Click(Mid(B(r1)));
            check(main.SelectedElement != e1 && Fill(main.SelectedElement) == "800080", "painter: clicking a shape gives it the format");

            Png(deck.Renderer.Render(sp), System.IO.Path.Combine(outDir, "edit-mouse.png"));
        }
        finally { main.CloseForTest(); }
    }
}
