using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using DocumentFormat.OpenXml.Packaging;
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
            main.Deck.Save(path, null);
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

    /// <summary>Ink is kept per slide id outside the deck and comes back when the same file is opened again.</summary>
    static void InkStoreTests(string file, string outDir, Action<bool, string> check)
    {
        var deckPath = Path.Combine(outDir, "ink.pptx");
        File.Copy(file, deckPath, true);

        var hi = Line(10, 10, 300, 10);
        hi.DrawingAttributes.IsHighlighter = true;
        hi.DrawingAttributes.Color = Colors.Yellow;
        InkStore.Save(deckPath, [(256u, new StrokeCollection { Line(1, 2, 3, 4), Line(5, 6, 7, 8) }), (300u, new StrokeCollection { hi })]);
        var kept = InkStore.Load(deckPath);
        var got = kept.GetValueOrDefault(300u)?.FirstOrDefault()?.DrawingAttributes;
        var pt = kept.GetValueOrDefault(256u)?.ElementAtOrDefault(1)?.StylusPoints[1];
        check(kept.Count == 2 && kept[256].Count == 2 && kept[300].Count == 1 && pt is { } p && Math.Abs(p.X - 7) < 0.05 && Math.Abs(p.Y - 8) < 0.05
              && got is { IsHighlighter: true } && got.Color == Colors.Yellow,
            $"ink store: strokes, points, color and highlighter come back per slide ({kept.Count} slides, point {pt?.X:0.###},{pt?.Y:0.###}, {got?.Color}, highlighter {got?.IsHighlighter})");
        InkStore.Save(deckPath, []);
        check(!File.Exists(InkStore.FileFor(deckPath)) && InkStore.Load(deckPath).Count == 0, "ink store: no ink = no file");

        var main = new MainWindow();
        try
        {
            main.Open(deckPath);
            var deck = main.Deck!;
            main.StrokesOf(deck.Slides[0]).Add(Line(20, 20, 200, 60));
            main.StrokesOf(deck.Slides[2]).Add(Line(30, 300, 400, 320));
            Wait(1400);
            check(File.Exists(InkStore.FileFor(deckPath)) && InkStore.Load(deckPath).Values.Sum(s => s.Count) == 2, "ink store: written a second after drawing");
            main.StrokesOf(deck.Slides[2]).Add(Line(50, 350, 450, 380));
            deck.NewSlide(0); // slide 3 becomes slide 4: ink follows the slide id, not the position
            deck.Save(deckPath, null);
            main.FlushInk();
        }
        finally { main.CloseForTest(); }

        main = new MainWindow();
        try
        {
            main.Open(deckPath);
            var deck = main.Deck!;
            int Count(int i) => main.StrokesOf(deck.Slides[i]).Count;
            check(Count(0) == 1 && Count(1) == 0 && Count(2) == 0 && Count(3) == 2, $"ink store: reopened file gets its ink back on the same slides ({Count(0)}, {Count(1)}, {Count(2)}, {Count(3)})");
            check(main.StatusText.Text.Contains("3획"), $"ink store: says so [{main.StatusText.Text}]");
            main.Go(3);
            main.Run("undo");
            check(Count(3) == 2, "ink store: restored ink is not undone by Ctrl+Z");
            foreach (var sp in deck.Slides) main.StrokesOf(sp).Clear();
            main.FlushInk();
            check(!File.Exists(InkStore.FileFor(deckPath)), "ink store: clearing all ink removes the kept file");
        }
        finally { main.CloseForTest(); }
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
