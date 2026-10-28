using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using P = DocumentFormat.OpenXml.Presentation;

namespace PptSlider;

static partial class SelfTest
{
    /// <summary>
    /// Slide show keys and mouse, driven in-process on an off-screen show window: no real keyboard, focus or
    /// full-screen window is involved, so the test can't type into other apps. Screenshots go to outDir/show-*.png.
    /// </summary>
    static void ShowTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow();
        try
        {
            main.Open(file);
            int n = main.Count;
            main.StartShow(0, test: true);
            var show = main.ShowView!;
            Wait(300);
            void Press(Key k, ModifierKeys m = ModifierKeys.None) => show.Press(new Gesture(k, m));
            const ModifierKeys Ctrl = ModifierKeys.Control;

            // blank screen
            Press(Key.B);
            check(main.Blanked == Blank.Black && show.Cover.IsVisible, "show: B = black screen");
            Snap(show.Root, Path.Combine(outDir, "show-black.png"));
            Press(Key.B);
            check(main.Blanked == Blank.None && !show.Cover.IsVisible, "show: B again = slide back");
            Press(Key.W);
            var slideInk = main.StrokesOf(main.CurrentSlide!);
            int before = slideInk.Count;
            main.ShowInk.Add(Line(100, 100, 500, 300));
            check(main.Blanked == Blank.White && show.Ink.Strokes == main.BlankInk && main.BlankInk.Count == 1 && slideInk.Count == before,
                "show: W = white screen, ink goes on the white screen, not the slide");
            Snap(show.Root, Path.Combine(outDir, "show-white-ink.png"));
            Press(Key.Right);
            check(main.Blanked == Blank.None && main.Current == 0 && show.Ink.Strokes == slideInk, "show: → on a blank screen only brings the slide back");
            Press(Key.OemPeriod);
            check(main.Blanked == Blank.Black, "show: . = black screen");
            Press(Key.OemComma);
            check(main.Blanked == Blank.White, "show: , = white screen (switches from black)");
            Press(Key.OemComma);
            check(main.Blanked == Blank.None, "show: , again = slide back");

            // laser pointer
            Press(Key.L, Ctrl);
            show.PointerMoved(new Point(640, 360));
            check(main.Pen.Tool == Tool.Laser && show.LaserDot.IsVisible && show.Root.Cursor == Cursors.None && !show.Ink.IsHitTestVisible,
                "show: Ctrl+L = laser dot, cursor hidden, no inking");
            show.UpdateLayout();
            var dot = show.LaserDot.TranslatePoint(new Point(show.LaserDot.Width / 2, show.LaserDot.Height / 2), show.Root);
            check((dot - new Point(640, 360)).Length < 2, $"show: laser dot under the mouse ({dot.X:0},{dot.Y:0})");
            Snap(show.Root, Path.Combine(outDir, "show-laser.png"));
            int at = main.Current;
            Press(Key.P);
            check(main.Pen.Tool == Tool.Pen && !show.LaserDot.IsVisible, "show: pen replaces the laser");
            Press(Key.L, Ctrl);
            Press(Key.L, Ctrl);
            check(main.Pen.Tool == Tool.Select && !show.LaserDot.IsVisible && main.Current == at, "show: Ctrl+L twice = back to the pointer");

            // go to a slide by number
            Press(Key.D3);
            check(show.JumpBox.IsVisible && show.JumpText.Text.EndsWith("3"), $"show: typed number shown [{show.JumpText.Text}]");
            Press(Key.Enter);
            check(main.Current == Math.Min(2, n - 1) && !show.JumpBox.IsVisible, "show: 3 Enter = slide 3");
            Press(Key.D1);
            Press(Key.NumPad2);
            Snap(show.Root, Path.Combine(outDir, "show-jump.png"));
            Wait(1300);
            check(main.Jump == "12", "show: two digits wait for Enter past a second");
            Press(Key.Back);
            Press(Key.D4);
            Press(Key.Enter);
            check(main.Current == Math.Min(13, n - 1), $"show: 1 2 Backspace 4 Enter = slide 14 (at {main.Current + 1})");
            Press(Key.D9); Press(Key.D9); Press(Key.D9); Press(Key.Enter);
            check(main.Current == n - 1, "show: a number past the end = last slide");
            Press(Key.D5);
            Press(Key.Escape);
            check(main.Jump == "" && main.Current == n - 1 && show.IsVisible, "show: Esc drops the number, the show stays");
            Press(Key.Home);
            Press(Key.D3);
            Wait(1300);
            check(main.Jump == "" && main.Pen.Tool == Tool.Pen && main.Pen.PenColor == PenState.Palette[2] && main.Current == 0,
                "show: a lone 3 = pen color 3 after a second");

            // ink undo
            var s = main.ShowInk;
            s.Add(Line(10, 10, 200, 200));
            s.Add(Line(50, 300, 500, 320));
            Press(Key.Z, Ctrl);
            check(s.Count == 1, "show: Ctrl+Z takes back the last stroke");
            var keep = s[0];
            s.Remove(keep); // what the eraser does
            Press(Key.Z, Ctrl);
            check(s.Count == 1 && s[0] == keep, "show: Ctrl+Z brings back an erased stroke");
            s.Add(Line(300, 50, 600, 80));
            Press(Key.C);
            check(s.Count == 0, "show: C clears the slide's ink");
            Press(Key.Z, Ctrl);
            check(s.Count == 2, "show: Ctrl+Z after C brings it all back");
            s.Add(Line(100, 500, 600, 520));
            Click(show.TUndo);
            check(s.Count == 2, "show: the undo button takes back the last stroke");
            Click(show.TUndo); Click(show.TUndo); Click(show.TUndo);
            check(s.Count == 0, "show: pressing it again goes further back, and stops at none");

            // cursor hides after 3 seconds still (pointer mode)
            Press(Key.Escape);
            show.PointerMoved(new Point(100, 100));
            check(main.Pen.Tool == Tool.Select && show.Root.Cursor == Cursors.Arrow, "show: pointer visible after moving");
            Wait(3300);
            check(show.Root.Cursor == Cursors.None, "show: pointer hidden after 3 s still");
            show.PointerMoved(new Point(120, 100));
            check(show.Root.Cursor == Cursors.Arrow, "show: moving shows it again");

            // clock and elapsed time
            check(Regex.IsMatch(show.Clock.Text, @"\d{1,2}:\d{2}\s+경과 00:\d{2}$"), $"show: clock and elapsed time [{show.Clock.Text}]");
            Snap(show.Root, Path.Combine(outDir, "show-clock.png"));

            show.Close();
            check(main.ShowView == null && main.Blanked == Blank.None && main.Jump == "", "show: closing resets the show state");

            // editor: Ctrl+Z takes back the slide's strokes first (newest first), then the edit
            var slide = main.CurrentSlide!;
            P.Shape Text() => slide.Slide!.Descendants<P.Shape>().First(sh => sh.TextBody != null);
            var original = Deck.GetText(Text());
            main.Deck!.Snapshot(slide);
            Deck.SetText(Text(), "편집됨");
            var ed = main.StrokesOf(slide);
            int strokes = ed.Count;
            ed.Add(Line(20, 400, 400, 420));
            main.Run("undo");
            check(ed.Count == strokes && Deck.GetText(Text()) == "편집됨", "editor: Ctrl+Z takes back the new stroke, not the edit");
            for (int i = 0; i < strokes + 1; i++) main.Run("undo");
            check(ed.Count == 0 && Deck.GetText(Text()) == original, $"editor: then the older strokes, then the edit ({ed.Count} strokes left)");

            // editor: the 그리기 tab's undo button takes back strokes only, never the edit
            main.Deck!.Snapshot(slide);
            Deck.SetText(Text(), "편집됨");
            ed.Add(Line(20, 400, 400, 420));
            ed.Add(Line(20, 450, 400, 470));
            Click(main.UndoInkBtn);
            check(ed.Count == 1 && main.StatusText.Text == "판서를 되돌렸습니다", "editor: the undo-ink button takes back the last stroke");
            Click(main.UndoInkBtn); Click(main.UndoInkBtn);
            check(ed.Count == 0 && Deck.GetText(Text()) == "편집됨" && main.StatusText.Text == "되돌릴 판서가 없습니다",
                "editor: with no strokes left it says so and leaves the edit alone");
        }
        finally { main.CloseForTest(); }
    }

    /// <summary>Speaker notes come from the notes page's body placeholder only (not the slide image or number placeholders).</summary>
    /// <returns>The deck saved with that notes page on slide 2, for the presenter view test.</returns>
    static string NotesTest(string file, string outDir, Action<bool, string> check)
    {
        using var deck = Deck.Open(file);
        var withNotes = deck.Slides.Count(s => Deck.Notes(s) != "");
        var slide = deck.Duplicate(0); // a copy has no notes page yet
        check(Deck.Notes(slide) == "", "notes: a slide without a notes page has none");
        var notes = slide.AddNewPart<DocumentFormat.OpenXml.Packaging.NotesSlidePart>();
        static string Ph(int id, string type, string text) =>
            $"""<p:sp><p:nvSpPr><p:cNvPr id="{id}" name="{type}"/><p:cNvSpPr/><p:nvPr><p:ph type="{type}"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/>{text}</p:txBody></p:sp>""";
        notes.NotesSlide = new P.NotesSlide($"""
            <p:notes xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
            <p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>
            {Ph(2, "sldImg", "<a:p/>")}
            {Ph(3, "body", "<a:p><a:r><a:t>첫 줄</a:t></a:r></a:p><a:p><a:r><a:t>둘째 </a:t></a:r><a:r><a:rPr b=\"1\"/><a:t>줄</a:t></a:r></a:p>")}
            {Ph(4, "sldNum", "<a:p><a:r><a:t>4</a:t></a:r></a:p>")}
            </p:spTree></p:cSld></p:notes>
            """);
        check(Deck.Notes(slide) == "첫 줄\n둘째 줄", $"notes: body placeholder text, one line per paragraph [{Deck.Notes(slide).Replace("\n", "\\n")}]");
        check(true, $"notes: {withNotes} of {deck.Slides.Count - 1} slides in the deck have notes");
        var path = Path.Combine(outDir, "notes.pptx");
        deck.Save(path, null);
        using var saved = Deck.Open(path);
        check(Deck.Notes(saved.Slides[1]) == "첫 줄\n둘째 줄", "notes: kept after save and reopen");
        return path;
    }

    /// <summary>The presenter view follows the show and drives it: keys, blank screen, laser, shared ink.</summary>
    static void PresenterTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow();
        try
        {
            main.Open(file);
            int n = main.Count;
            main.StartShow(0, test: true);
            var show = main.ShowView!;
            var pv = main.PresenterView;
            check(pv != null, "presenter: opens with the show");
            if (pv == null) return;
            Wait(300);
            void Press(Key k, ModifierKeys m = ModifierKeys.None) => pv.Press(new Gesture(k, m));
            string Expected() => Deck.Notes(main.CurrentSlide!) is { Length: > 0 } t ? t : "(노트 없음)";

            check(pv.Page.Text == $"1 / {n}" && pv.Host.Content != null && pv.NextHost.Content != null && pv.Notes.Text == Expected(),
                "presenter: current slide, next slide, notes, page number");
            check(Regex.IsMatch(pv.Elapsed.Text, @"^00:0\d$") && Regex.IsMatch(pv.Clock.Text, @"\d{1,2}:\d{2}"), $"presenter: elapsed {pv.Elapsed.Text}, clock {pv.Clock.Text}");
            Snap(pv, Path.Combine(outDir, "presenter.png"));
            Press(Key.Right);
            check(main.Current == 1 && show.Page.Text == $"2 / {n}" && pv.Page.Text == $"2 / {n}" && pv.Notes.Text == Expected(),
                "presenter: → moves the show and updates the notes");
            if (main.Deck!.Slides.FindIndex(s => Deck.Notes(s) != "") is >= 0 and var k)
            {
                main.Go(k);
                check(pv.Notes.Text == Deck.Notes(main.CurrentSlide!), $"presenter: slide {k + 1}'s notes shown");
                Snap(pv, Path.Combine(outDir, "presenter-notes.png"));
            }

            main.ShowInk.Add(Line(50, 50, 300, 120));
            check(pv.Ink.Strokes == show.Ink.Strokes && show.Ink.Strokes.Count > 0, "presenter: same ink as the show");
            int before = main.ShowInk.Count;
            main.ShowInk.Add(Line(60, 200, 400, 220));
            Click(pv.TUndo);
            check(main.ShowInk.Count == before && show.Ink.Strokes.Count == before, "presenter: the undo button takes back the last stroke on both screens");
            Press(Key.B);
            check(show.Cover.IsVisible && pv.Cover.IsVisible && pv.TBlack.IsChecked == true && pv.State.Text.Contains("검은"), "presenter: B blanks the show, presenter says so");
            Snap(pv, Path.Combine(outDir, "presenter-black.png"));
            Press(Key.B);
            check(!show.Cover.IsVisible && !pv.Cover.IsVisible && pv.State.Text == "", "presenter: B again brings it back");

            Press(Key.L, ModifierKeys.Control);
            pv.PointerMoved(new Point(100, 50));
            show.UpdateLayout();
            check(main.LaserAt == new Point(100, 50) && show.LaserDot.IsVisible && pv.LaserDot.IsVisible
                  && Math.Abs(System.Windows.Controls.Canvas.GetLeft(show.LaserDot) + show.LaserDot.Width / 2 - 100) < 0.01,
                "presenter: the laser on the presenter's slide shows on the show at the same place");
            pv.PointerMoved(new Point(-5, 10));
            check(main.LaserAt == null && !show.LaserDot.IsVisible, "presenter: off the slide = no laser dot");
            Press(Key.L, ModifierKeys.Control);

            Press(Key.D2); Press(Key.Enter);
            check(main.Current == 1 && pv.Page.Text == $"2 / {n}", "presenter: number + Enter works here too");
            Press(Key.End);
            check(pv.NextEnd.IsVisible && !pv.NextHost.IsVisible, "presenter: last slide says there is no next one");
            Press(Key.Escape);
            check(main.ShowView == null && main.PresenterView == null, "presenter: Esc ends the show and closes the presenter view");

            main.PresenterBox.IsChecked = false;
            main.StartShow(0, test: true);
            check(main.ShowView != null && main.PresenterView == null, "presenter: off when [발표자 보기 사용] is unchecked");
            main.ShowView?.Close();
        }
        finally { main.CloseForTest(); }
    }

    /// <summary>Z, drag an area: it fills the show; ink still works; Esc, a click or another slide goes back.</summary>
    static void ZoomTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow();
        try
        {
            main.Open(file);
            main.PresenterBox.IsChecked = false;
            main.StartShow(0, test: true);
            var show = main.ShowView!;
            Wait(300);
            void Press(Key k, ModifierKeys m = ModifierKeys.None) => show.Press(new Gesture(k, m));
            double w = show.Stage.Width, h = show.Stage.Height;
            Point OnRoot(double x, double y) => show.Stage.TranslatePoint(new Point(x, y), show.Root);
            Point OnFrame(double x, double y) => show.Stage.TranslatePoint(new Point(x, y), show.Frame);
            bool Near(Point a, double x, double y) => Math.Abs(a.X - x) < 0.5 && Math.Abs(a.Y - y) < 0.5;

            Press(Key.Z);
            check(show.Zooming && !show.Zoomed && show.ZoomHint.IsVisible && show.Root.Cursor == Cursors.Cross, "zoom: Z = pick an area (hint, cross cursor)");
            show.PickDown(OnRoot(w * 0.25, h * 0.25));
            show.PickMove(OnRoot(w * 0.4, h * 0.4));
            check(show.PickBox.IsVisible, "zoom: the dragged area is drawn");
            show.PickUp(OnRoot(w * 0.5, h * 0.5));
            show.UpdateLayout();
            check(show.Zoomed && Near(OnFrame(w * 0.25, h * 0.25), 0, 0) && Near(OnFrame(w * 0.5, h * 0.5), w, h) && !show.ZoomHint.IsVisible,
                $"zoom: the area fills the screen ({OnFrame(w * 0.25, h * 0.25):0.#} – {OnFrame(w * 0.5, h * 0.5):0.#})");
            Press(Key.P);
            check(show.Ink.IsHitTestVisible && show.Zoomed, "zoom: the pen draws while zoomed");
            main.ShowInk.Add(Line(w * 0.3, h * 0.3, w * 0.45, h * 0.35));
            Snap(show.Root, Path.Combine(outDir, "show-zoom.png"));
            Press(Key.Escape);
            show.UpdateLayout();
            check(!show.Zooming && show.IsVisible && main.Pen.Tool == Tool.Pen && Near(OnFrame(w, h), w, h), "zoom: Esc = whole slide, the pen and the show stay");
            Press(Key.Escape);

            Press(Key.Z);
            show.PickDown(OnRoot(0, 0));
            show.PickUp(OnRoot(w / 8, h / 8));
            show.UpdateLayout();
            check(show.Zoomed && Near(OnFrame(0, 0), 0, 0) && show.LaserDot.Width < w / 80 / 7, "zoom: a corner zooms 8× without showing past the edge, laser dot keeps its size");
            int at = main.Current;
            main.ShowAdvance(1); // a click in pointer mode
            check(!show.Zooming && main.Current == at, "zoom: a click goes back to the whole slide instead of moving on");

            Press(Key.Z);
            show.PickDown(OnRoot(100, 100));
            show.PickUp(OnRoot(102, 101));
            check(show.Zooming && !show.Zoomed, "zoom: a click without a drag keeps picking");
            Press(Key.Z);
            check(!show.Zooming, "zoom: Z again cancels");

            Press(Key.Z);
            show.PickDown(OnRoot(w * 0.1, h * 0.1));
            show.PickUp(OnRoot(w * 0.6, h * 0.6));
            Press(Key.Right);
            check(!show.Zooming && main.Current == at + 1, "zoom: the next slide starts whole");
            show.Close();
        }
        finally { main.CloseForTest(); }
    }

    /// <summary>Screenshots of the editor with each ribbon tab, off screen: outDir/editor-tab*.png.</summary>
    static void EditorSnaps(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, Width = 1400, Height = 860, ShowActivated = false };
        try
        {
            main.Show();
            main.Open(file);
            for (int i = 0; i < main.Ribbon.Items.Count; i++)
            {
                main.Ribbon.SelectedIndex = i;
                Wait(150);
                Snap(main, Path.Combine(outDir, $"editor-tab{i + 1}.png"));
            }
            check(true, $"editor: {main.Ribbon.Items.Count} ribbon tabs captured");
        }
        finally { main.CloseForTest(); }
    }

    static Stroke Line(double x1, double y1, double x2, double y2) =>
        new(new StylusPointCollection { new StylusPoint(x1, y1), new StylusPoint(x2, y2) })
        {
            DrawingAttributes = { Color = Colors.Red, Width = 6, Height = 6 },
        };

    /// <summary>A button click as the user makes it (the Click handler runs; no mouse needed).</summary>
    static void Click(System.Windows.Controls.Button b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    /// <summary>Lets timers and layout run for <paramref name="ms"/> milliseconds.</summary>
    static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
        t.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Screenshot of an element already laid out in a window.</summary>
    static void Snap(FrameworkElement el, string path)
    {
        el.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)el.ActualWidth, (int)el.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}
