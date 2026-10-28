using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace PptSlider;

static partial class SelfTest
{
    /// <summary>Ctrl+Enter puts an empty slide after the current one, in the editor and in the show, and it is saved.</summary>
    static void BlankPageTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow();
        try
        {
            main.Open(file);
            int n = main.Count;
            var blankLayout = main.Deck!.Pres.SlideMasterParts.SelectMany(m => m.SlideLayoutParts).Any(l => l.SlideLayout?.Type?.Value == P.SlideLayoutValues.Blank);
            main.Run("blankPage");
            var page = main.CurrentSlide!;
            check(main.Count == n + 1 && main.Current == 1 && main.Pen.Tool == Tool.Pen, "blank page: Ctrl+Enter in the editor adds a slide after the current one, pen ready");
            check(!page.Slide!.CommonSlideData!.ShapeTree!.Elements<P.Shape>().Any() && !page.Slide.CommonSlideData.ShapeTree.Elements<P.Picture>().Any()
                  && (!blankLayout || page.SlideLayoutPart?.SlideLayout?.Type?.Value == P.SlideLayoutValues.Blank),
                $"blank page: no shapes of its own{(blankLayout ? ", the master's Blank layout" : " (deck has no Blank layout)")}");
            Png(main.Deck.Renderer.Render(page), Path.Combine(outDir, "blank-page.png"));

            main.StartShow(1, test: true);
            var show = main.ShowView!;
            Wait(200);
            show.Press(new Gesture(Key.Enter, ModifierKeys.Control));
            check(main.Count == n + 2 && main.Current == 2 && show.Page.Text == $"3 / {n + 2}", "blank page: Ctrl+Enter in the show too, and the show moves there");
            show.Press(new Gesture(Key.D5, ModifierKeys.None));
            show.Press(new Gesture(Key.Enter, ModifierKeys.Control));
            check(main.Count == n + 3 && main.Jump == "", "blank page: Ctrl+Enter is not the Enter of a typed number");
            show.Close();

            var path = Path.Combine(outDir, "blank-pages.pptx");
            main.Deck.Save(path);
            check(Slides(path) == n + 3, "blank page: saved as real slides");
        }
        finally { main.CloseForTest(); }
    }

    /// <summary>Shift + drag: the stroke becomes a line from its first to its last point.</summary>
    static void StraightTests(string file, string outDir, Action<bool, string> check)
    {
        StylusPointCollection Wavy() => new(Enumerable.Range(0, 30).Select(i => new StylusPoint(10 + i * 10, 100 + 20 * Math.Sin(i))));
        var s = new Stroke(Wavy());
        var (first, last) = (s.StylusPoints[0], s.StylusPoints[^1]);
        PenState.Straighten(s);
        check(s.StylusPoints.Count == 2 && s.StylusPoints[0] == first && s.StylusPoints[1] == last, "straight: Shift stroke = line from start to end");

        var ic = new InkCanvas();
        PenState.StraightenOnShift(ic);
        var free = new Stroke(Wavy());
        ic.Strokes.Add(free);
        ic.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(free) { RoutedEvent = InkCanvas.StrokeCollectedEvent });
        check(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || free.StylusPoints.Count == 30, "straight: without Shift the stroke stays as drawn");
    }

    /// <summary>
    /// Screen ink: the overlay shows while a pen is picked or the slide has ink, each slide brings back its own strokes,
    /// and strokes drawn over the slide land on it in the PDF.
    /// </summary>
    static void OverlayTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow();
        try
        {
            main.Open(file);
            var deck = main.Deck!;
            check(main.InkView is not { IsVisible: true }, "overlay: none while pointing with no ink");
            main.Run("pen");
            var ov = main.InkView;
            check(ov is { IsVisible: true } && ov.Ink.IsHitTestVisible && ov.Over == null && ov.Strokes == main.StrokesOf(deck.Slides[0]),
                "overlay: the pen shows it over the editor, drawing on slide 1's strokes");
            if (ov == null) return;
            ov.Strokes.Add(Line(100, 100, 400, 200));
            main.Go(1);
            check(ov.IsVisible && ov.Strokes == main.StrokesOf(deck.Slides[1]) && ov.Strokes.Count == 0, "overlay: next slide = its own (empty) ink");
            main.Go(0);
            check(ov.Strokes.Count == 1, "overlay: back on slide 1 its ink comes back");
            ov.Press(new Gesture(Key.Escape, ModifierKeys.None));
            check(main.Pen.Tool == Tool.Select && ov.IsVisible && !ov.Ink.IsHitTestVisible, "overlay: Esc = pointer, the ink stays and clicks go through");
            main.Go(1);
            check(!ov.IsVisible, "overlay: pointer on a slide without ink hides it");
            main.Go(0);
            check(ov.IsVisible, "overlay: and a slide with ink shows it again");
            main.HideInk();
            check(!ov.IsVisible && main.InkHidden && main.InkShownBtn.IsChecked == false, "overlay: ✕ hides the ink");
            main.Run("pen");
            check(ov.IsVisible && !main.InkHidden, "overlay: picking the pen shows it again");
            main.Run("undo");
            check(ov.Strokes.Count == 0, "overlay: Ctrl+Z takes back the stroke");
            main.Run("undo");
            main.Run("select");

            // slide coordinates: a stroke drawn over the show's slide lands on the same spot of the slide
            main.StartShow(0, test: true);
            var show = main.ShowView!;
            Wait(200);
            main.Run("pen");
            ov = main.InkView!;
            check(ov.IsVisible && ov.Over == show, "overlay: during the show it sits over the show");
            Point On(double x, double y) => ov.PointFromScreen(show.Stage.PointToScreen(new Point(x, y)));
            var st = new Stroke(new StylusPointCollection { new StylusPoint(On(100, 80).X, On(100, 80).Y), new StylusPoint(On(500, 300).X, On(500, 300).Y) })
            {
                DrawingAttributes = { Color = Colors.Red, Width = 6, Height = 6 },
            };
            main.TagStroke(st, ov);
            main.OverlayInk.Add(st);
            main.OverlayInk.Add(Line(0, 0, 10, 10)); // not tagged (drawn over another app): kept on screen, not on the slide
            var slideInk = main.SlideInk();
            var got = slideInk.GetValueOrDefault(deck.Slides[0]);
            var p0 = got?.FirstOrDefault()?.StylusPoints[0];
            var p1 = got?.FirstOrDefault()?.StylusPoints[1];
            check(slideInk.Count == 1 && got!.Count == 1 && p0 is { } a && p1 is { } b && Math.Abs(a.X - 100) < 1 && Math.Abs(a.Y - 80) < 1 && Math.Abs(b.X - 500) < 1 && Math.Abs(b.Y - 300) < 1,
                $"overlay: ink on the show's slide maps onto the slide ({p0?.X:0.#},{p0?.Y:0.#} – {p1?.X:0.#},{p1?.Y:0.#})");
            show.Close();
            Wait(100);
            check(main.InkView is { Over: null }, "overlay: after the show it goes back over the editor");

            var notes = Path.Combine(outDir, "ink-notes.pdf");
            PdfExport.Export(deck, notes, slideInk, onlyInked: true);
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(notes));
            check(Regex.Matches(text, @"/Type /Page\b(?!s)").Count == 1, "ink notes: only the slide with ink is a page");

            main.ClearAllInk();
            check(main.SlideInk().Count == 0, "overlay: clearing all ink clears every slide");
        }
        finally { main.CloseForTest(); }
    }

    /// <summary>A small PNG (a gradient) for the picture tests.</summary>
    static byte[] TinyPng()
    {
        var px = new byte[40 * 20 * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = (byte)(i % 255); px[i + 1] = 120; px[i + 2] = 200; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgra32, null, px, 40 * 4)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Insert picture: centered at its own size, selected, undoable; other files are refused.</summary>
    static void PictureTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow();
        try
        {
            main.Open(file);
            var sp = main.CurrentSlide!;
            int Pics() => sp.Slide!.Descendants<P.Picture>().Count();
            int before = Pics();
            check(main.InsertPicture(TinyPng(), Deck.PictureType(".png")!.Value), "picture: inserted");
            var hits = new List<ShapeHit>();
            main.Deck!.Renderer.Render(sp, hits);
            var h = hits.LastOrDefault(x => x.Element is P.Picture);
            double w = main.Deck.Renderer.Width, ht = main.Deck.Renderer.Height;
            check(Pics() == before + 1 && h != null && Math.Abs(h.Bounds.Width - 40) < 0.5 && Math.Abs(h.Bounds.X + 20 - w / 2) < 0.5 && Math.Abs(h.Bounds.Y + 10 - ht / 2) < 0.5
                  && main.SelectedElement == h.Element && main.Deck.Dirty,
                $"picture: own size, centered, selected ({h?.Bounds})");
            main.Run("undo");
            check(Pics() == before, "picture: Ctrl+Z takes it out");
            var txt = Path.Combine(outDir, "not-a-picture.txt");
            File.WriteAllText(txt, "x");
            check(!main.InsertPictureFile(txt) && Pics() == before, "picture: other files are ignored");
        }
        finally { main.CloseForTest(); }
    }

    /// <summary>Dragging a thumbnail moves the slide; the order is saved.</summary>
    static void ReorderTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow();
        try
        {
            main.Open(file);
            var deck = main.Deck!;
            if (deck.Slides.Count < 4) { check(true, "reorder: deck too small, skipped"); return; }
            var order = deck.Slides.ToList();
            main.MoveSlide(0, 3); // before the slide at 3: ends up third
            check(deck.Slides[2] == order[0] && deck.Slides[0] == order[1] && deck.Slides[1] == order[2] && main.Current == 2 && deck.Dirty,
                "reorder: slide 1 dropped before slide 4 becomes slide 3, and is shown");
            main.MoveSlide(2, 0);
            check(deck.Slides.SequenceEqual(order), "reorder: and back to the top");
            main.MoveSlide(0, deck.Slides.Count);
            check(deck.Slides[^1] == order[0], "reorder: dropped below the last = the end");
            var path = Path.Combine(outDir, "reordered.pptx");
            var first = Deck.GetText(deck.Slides[^1].Slide!.Descendants<P.Shape>().First(s => Deck.HasText(s)));
            deck.Save(path);
            using var saved = Deck.Open(path);
            check(saved.Slides.Count == order.Count && saved.Slides[^1].Slide!.Descendants<P.Shape>().Any(s => Deck.HasText(s) && Deck.GetText(s) == first),
                "reorder: the order is saved");
        }
        finally { main.CloseForTest(); }
    }

    /// <summary>normAutofit ("shrink text on overflow"): text too big for its box is drawn smaller until it fits.</summary>
    static void AutofitTests(string file, string outDir, Action<bool, string> check)
    {
        using var deck = Deck.Open(file);
        var sp = deck.Slides[0];
        var r = new Rect(60, 60, 300, 80);
        var shape = Deck.AddTextBox(sp, r, string.Join("\n", Enumerable.Range(1, 5).Select(i => $"줄 {i} — 넘치는 텍스트")));
        var body = shape.Descendants<A.BodyProperties>().First();
        body.RemoveAllChildren();
        body.Append(new A.NormalAutoFit());
        Deck.EditRuns(shape, run => Deck.SetAttr(run, "sz", "2400"));
        var hits = new List<ShapeHit>();
        var el = deck.Renderer.Render(sp, hits);
        var h = hits.First(x => x.Element == shape);
        var t = h.Text!;
        t.Measure(new Size(r.Width, double.PositiveInfinity));
        check(t.Tag is double s && s < 1 && s >= 0.25 && t.DesiredSize.Height <= r.Height + 1, $"autofit: shrunk to {t.Tag:0.##} so it fits ({t.DesiredSize.Height:0} ≤ {r.Height})");
        Png(el, Path.Combine(outDir, "autofit.png"));
        body.RemoveAllChildren();
        body.Append(new A.NoAutoFit());
        deck.Renderer.Render(sp, hits = []);
        check(hits.First(x => x.Element == shape).Text!.Tag is 1.0, "autofit: without normAutofit the size stays");
    }

    /// <summary>PDF: valid structure (header, xref offsets, one page and one JPEG per slide), ink only when asked.</summary>
    static void PdfTests(string file, string outDir, Action<bool, string> check)
    {
        using var deck = Deck.Open(file);
        int n = deck.Slides.Count;
        var plain = Path.Combine(outDir, "export.pdf");
        var inked = Path.Combine(outDir, "export-ink.pdf");
        var ink = new Dictionary<SlidePart, StrokeCollection> { [deck.Slides[0]] = new() { Line(50, 50, 600, 400) } };
        PdfExport.Export(deck, plain, null);
        PdfExport.Export(deck, inked, ink);

        foreach (var path in new[] { plain, inked })
        {
            var bytes = File.ReadAllBytes(path);
            var text = Encoding.Latin1.GetString(bytes);
            var pages = Regex.Matches(text, @"/Type /Page\b(?!s)").Count;
            var images = Regex.Matches(text, @"/Subtype /Image .*?/Filter /DCTDecode").Count;
            int xref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value);
            var entries = Regex.Matches(text[xref..], @"(\d{10}) 00000 n ").Select(m => int.Parse(m.Groups[1].Value)).ToList();
            bool offsetsOk = text[xref..].StartsWith("xref") && entries.Select((o, i) => text[o..].StartsWith($"{i + 1} 0 obj")).All(b => b);
            bool jpegs = Regex.Matches(text, "(?<!end)stream\n").All(m => bytes[m.Index + m.Length] is 0xFF or (byte)'q');
            check(text.StartsWith("%PDF-1.4") && pages == n && images == n && offsetsOk && entries.Count == 2 + 3 * n && jpegs,
                $"pdf: {Path.GetFileName(path)} {bytes.Length / 1024} KB, {pages} pages for {n} slides, xref ok={offsetsOk}");
        }
        static byte[] FirstImage(string path)
        {
            var b = File.ReadAllBytes(path);
            var t = Encoding.Latin1.GetString(b);
            var m = Regex.Match(t, @"/DCTDecode /Length (\d+) >>\nstream\n");
            return b[(m.Index + m.Length)..(m.Index + m.Length + int.Parse(m.Groups[1].Value))];
        }
        check(!FirstImage(plain).SequenceEqual(FirstImage(inked)), "pdf: the ink version's first page differs");
    }
}
