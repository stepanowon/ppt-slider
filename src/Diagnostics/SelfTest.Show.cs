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

            // ink belongs to the show: in the editor the drawing tools are refused and there is no overlay
            main.Run("pen");
            main.Run("color2");
            check(main.Pen.Tool == Tool.Select && main.InkView is not { IsVisible: true } && !main.ToolPen.IsEnabled && !main.ToolHi.IsEnabled && !main.ToolEraser.IsEnabled,
                "ink: no pen, highlighter or eraser in the editor, only in the show");

            main.StartShow(0, test: true);
            var show = main.ShowView!;
            Wait(300);
            check(main.ToolPen.IsEnabled && main.ToolHi.IsEnabled && main.ToolEraser.IsEnabled, "ink: the ribbon's drawing tools work during the show");
            // pen → pointer hides the ink overlay (no ink yet); the pen again shows it. On screen the overlay is maximized,
            // and WPF throws when a maximized window that must not take focus is shown again (crashed 0.2.4)
            main.Run("pen");
            main.Run("select");
            var hiddenOverlay = main.InkView!;
            bool hidden = !hiddenOverlay.IsVisible;
            hiddenOverlay.WindowState = WindowState.Maximized; // hidden, so nothing moves on screen
            string? error = null;
            try { main.Run("pen"); } catch (InvalidOperationException e) { error = e.Message; }
            check(hidden && error == null && hiddenOverlay.IsVisible && hiddenOverlay.WindowState == WindowState.Normal,
                $"ink: the overlay comes back after the pointer hid it ({error ?? "ok"})");
            main.Run("select");
            // other windows (a browser...) can come in front of the show; ink over the desktop stays above every app
            check(!show.Topmost && !InkOverlay.StaysOnTop(show) && InkOverlay.StaysOnTop(null),
                "show: not always on top (nor its ink overlay); the desktop ink overlay is");
            void Press(Key k, ModifierKeys m = ModifierKeys.None) => show.Press(new Gesture(k, m));
            const ModifierKeys Ctrl = ModifierKeys.Control;

            // one ink toolbar: the show's, or the overlay's while the overlay is over the show (never both)
            bool OnlyShowBar() => show.Bar.IsVisible && main.InkView is not { IsVisible: true };
            bool OnlyOverlayBar() => main.InkView is { IsVisible: true } o && o.Over == show && o.Bar.IsVisible && !show.Bar.IsVisible;
            check(OnlyShowBar(), "show bar: pointer without ink = the show's toolbar only");
            Press(Key.P);
            var tb = main.InkView;
            check(OnlyOverlayBar() && tb!.ShowNav.IsVisible && tb.TLaser.IsVisible && tb.ShowEnd.IsVisible
                  && tb.Bar.HorizontalAlignment == HorizontalAlignment.Left && tb.Bar.VerticalAlignment == VerticalAlignment.Bottom,
                "show bar: drawing = the overlay's toolbar only, bottom left, with the show's buttons");
            Snap(tb!.Root, Path.Combine(outDir, "show-one-toolbar.png"));
            Click(tb.TNext);
            check(main.Current == 1 && OnlyOverlayBar(), "show bar: the overlay toolbar's → moves the show");
            main.OverlayInk.Add(Line(100, 100, 400, 200));
            Press(Key.Escape);
            check(main.Pen.Tool == Tool.Select && OnlyOverlayBar(), "show bar: pointer over a slide with ink = still only the overlay's toolbar");
            tb.TLaser.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(main.Pen.Tool == Tool.Laser && tb.TLaser.IsChecked == true && tb.TSelect.IsChecked != true, "show bar: the overlay toolbar's laser button");
            Press(Key.L, Ctrl);
            Press(Key.C);
            check(main.Pen.Tool == Tool.Select && OnlyShowBar(), "show bar: ink cleared in pointer mode = back to the show's toolbar");
            Press(Key.Home);
            Wait(100);

            // pen cursor: a real cursor, not InkCanvas's dot the size of the pen tip
            Press(Key.P);
            var ink = main.InkView!.Ink;
            check(ink.UseCustomCursor && ink.Cursor == Cursors.Pen, "show: the pen has a visible cursor");
            Press(Key.H);
            check(ink.UseCustomCursor && ink.Cursor == Cursors.Pen, "show: the highlighter has a visible cursor");
            Press(Key.E);
            check(!ink.UseCustomCursor, "show: the eraser keeps InkCanvas's eraser cursor");

            // spot highlight: S toggles a circle around the pointer; both toolbars show its state
            Press(Key.P);
            Press(Key.S);
            show.SpotAt(new Point(300, 200));
            check(main.Session.Spotlight && show.Spot.IsVisible && main.InkView!.TSpot.IsChecked == true && show.TSpot.IsChecked == true
                  && Math.Abs(System.Windows.Controls.Canvas.GetLeft(show.Spot) + show.Spot.Width / 2 - 300) < 0.5 && Math.Abs(System.Windows.Controls.Canvas.GetTop(show.Spot) + show.Spot.Height / 2 - 200) < 0.5,
                "spotlight: S = a circle centered on the pointer, also while drawing");
            Snap(show.Root, Path.Combine(outDir, "show-spotlight.png"));
            show.SpotAt(null);
            check(!show.Spot.IsVisible && main.Session.Spotlight, "spotlight: hidden while the pointer is off the show screen");
            Press(Key.Right);
            show.SpotAt(new Point(640, 360));
            check(show.Spot.IsVisible, "spotlight: stays on on the next slide");
            Press(Key.S);
            show.SpotAt(new Point(640, 360));
            check(!main.Session.Spotlight && !show.Spot.IsVisible && show.TSpot.IsChecked != true, "spotlight: S again = off");
            show.TSpot.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(main.Session.Spotlight, "spotlight: the show toolbar's button turns it on");
            main.InkView!.TSpot.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(!main.Session.Spotlight, "spotlight: the overlay toolbar's button turns it off");
            Press(Key.Escape);
            Press(Key.Home);

            // blank screen
            Press(Key.B);
            check(main.Session.Blanked == Blank.Black && show.Cover.IsVisible, "show: B = black screen");
            Snap(show.Root, Path.Combine(outDir, "show-black.png"));
            Press(Key.B);
            check(main.Session.Blanked == Blank.None && !show.Cover.IsVisible, "show: B again = slide back");
            Press(Key.W);
            var slideInk = main.Ink.Of(main.CurrentSlide!);
            int before = slideInk.Count;
            main.OverlayInk.Add(Line(100, 100, 500, 300));
            check(main.Session.Blanked == Blank.White && main.InkView is { IsVisible: true } ov && ov.Strokes == main.Ink.Blank && ov.Over == show && main.Ink.Blank.Count == 1 && slideInk.Count == before,
                "show: W = white screen, ink goes on the white screen (on the overlay over the show), not the slide");
            Snap(show.Root, Path.Combine(outDir, "show-white-ink.png"));
            Press(Key.Right);
            check(main.Session.Blanked == Blank.None && main.Current == 0 && main.InkView?.Strokes == slideInk, "show: → on a blank screen only brings the slide back");
            Press(Key.OemPeriod);
            check(main.Session.Blanked == Blank.Black, "show: . = black screen");
            Press(Key.OemComma);
            check(main.Session.Blanked == Blank.White, "show: , = white screen (switches from black)");
            Press(Key.OemComma);
            check(main.Session.Blanked == Blank.None, "show: , again = slide back");

            // laser pointer
            Press(Key.L, Ctrl);
            show.PointerMoved(new Point(640, 360));
            check(main.Pen.Tool == Tool.Laser && show.LaserDot.IsVisible && show.Root.Cursor == Cursors.None && main.InkView?.Ink.IsHitTestVisible != true,
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
            check(main.Session.Jump == "12", "show: two digits wait for Enter past a second");
            Press(Key.Back);
            Press(Key.D4);
            Press(Key.Enter);
            check(main.Current == Math.Min(13, n - 1), $"show: 1 2 Backspace 4 Enter = slide 14 (at {main.Current + 1})");
            Press(Key.D9); Press(Key.D9); Press(Key.D9); Press(Key.Enter);
            check(main.Current == n - 1, "show: a number past the end = last slide");
            Press(Key.D5);
            Press(Key.Escape);
            check(main.Session.Jump == "" && main.Current == n - 1 && show.IsVisible, "show: Esc drops the number, the show stays");
            Press(Key.Home);
            Press(Key.D3);
            Wait(1300);
            check(main.Session.Jump == "" && main.Pen.Tool == Tool.Pen && main.Pen.PenColor == PenState.Palette[2] && main.Current == 0,
                "show: a lone 3 = pen color 3 after a second");

            // ink undo
            var s = main.OverlayInk;
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
            check(main.ShowView == null && main.Session.Blanked == Blank.None && main.Session.Jump == "", "show: closing resets the show state");

            // editor: no ink on screen, so Ctrl+Z is the slide's (the ink the show left stays with the slide)
            var slide = main.CurrentSlide!;
            P.Shape Text() => slide.Slide!.Descendants<P.Shape>().First(sh => sh.TextBody != null);
            var original = TextContent.GetText(Text());
            main.Deck!.History.Snapshot(slide);
            TextContent.SetText(Text(), "편집됨");
            var ed = main.Ink.Of(slide);
            int strokes = ed.Count;
            ed.Add(Line(20, 400, 400, 420));
            main.Run("undo");
            check(ed.Count == strokes + 1 && TextContent.GetText(Text()) == original, "editor: Ctrl+Z takes back the edit, not the show's ink");
            while (main.Ink.Undo(ed)) { } // the strokes go (the undo-ink button's test below starts with none)

            // editor: the 그리기 tab's undo button takes back strokes only, never the edit
            main.Deck!.History.Snapshot(slide);
            TextContent.SetText(Text(), "편집됨");
            ed.Add(Line(20, 400, 400, 420));
            ed.Add(Line(20, 450, 400, 470));
            Click(main.UndoInkBtn);
            check(ed.Count == 1 && main.StatusText.Text == "판서를 되돌렸습니다", "editor: the undo-ink button takes back the last stroke");
            Click(main.UndoInkBtn); Click(main.UndoInkBtn);
            check(ed.Count == 0 && TextContent.GetText(Text()) == "편집됨" && main.StatusText.Text == "되돌릴 판서가 없습니다",
                "editor: with no strokes left it says so and leaves the edit alone");
        }
        finally { main.CloseForTest(); }
    }

    /// <summary>Speaker notes come from the notes page's body placeholder only (not the slide image or number placeholders).</summary>
    /// <returns>The deck saved with that notes page on slide 2, for the presenter view test.</returns>
    static string NotesTest(string file, string outDir, Action<bool, string> check)
    {
        using var deck = Deck.Open(file);
        var withNotes = deck.Slides.Count(s => TextContent.Notes(s) != "");
        var slide = deck.Duplicate(0); // a copy has no notes page yet
        check(TextContent.Notes(slide) == "", "notes: a slide without a notes page has none");
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
        check(TextContent.Notes(slide) == "첫 줄\n둘째 줄", $"notes: body placeholder text, one line per paragraph [{TextContent.Notes(slide).Replace("\n", "\\n")}]");
        check(true, $"notes: {withNotes} of {deck.Slides.Count - 1} slides in the deck have notes");
        var path = Path.Combine(outDir, "notes.pptx");
        deck.Save(path);
        using var saved = Deck.Open(path);
        check(TextContent.Notes(saved.Slides[1]) == "첫 줄\n둘째 줄", "notes: kept after save and reopen");
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
            string Expected() => TextContent.Notes(main.CurrentSlide!) is { Length: > 0 } t ? t : "(노트 없음)";

            check(pv.Page.Text == $"1 / {n}" && pv.Host.Content != null && pv.NextHost.Content != null && pv.Notes.Text == Expected(),
                "presenter: current slide, next slide, notes, page number");
            check(Regex.IsMatch(pv.Elapsed.Text, @"^00:0\d$") && Regex.IsMatch(pv.Clock.Text, @"\d{1,2}:\d{2}"), $"presenter: elapsed {pv.Elapsed.Text}, clock {pv.Clock.Text}");
            Snap(pv, Path.Combine(outDir, "presenter.png"));
            Press(Key.Right);
            check(main.Current == 1 && show.Page.Text == $"2 / {n}" && pv.Page.Text == $"2 / {n}" && pv.Notes.Text == Expected(),
                "presenter: → moves the show and updates the notes");
            if (main.Deck!.Slides.FindIndex(s => TextContent.Notes(s) != "") is >= 0 and var k)
            {
                main.Go(k);
                check(pv.Notes.Text == TextContent.Notes(main.CurrentSlide!), $"presenter: slide {k + 1}'s notes shown");
                Snap(pv, Path.Combine(outDir, "presenter-notes.png"));
            }

            main.OverlayInk.Add(Line(50, 50, 300, 120));
            check(main.InkView is { IsVisible: true } ov && ov.Over == show && ov.Strokes.Count > 0, "presenter: ink drawn goes on the show's screen");
            int before = main.OverlayInk.Count;
            main.OverlayInk.Add(Line(60, 200, 400, 220));
            Click(pv.TUndo);
            check(main.OverlayInk.Count == before && main.InkView!.Strokes.Count == before, "presenter: the undo button takes back the last stroke");
            Press(Key.B);
            check(show.Cover.IsVisible && pv.Cover.IsVisible && pv.TBlack.IsChecked == true && pv.State.Text.Contains("검은"), "presenter: B blanks the show, presenter says so");
            Snap(pv, Path.Combine(outDir, "presenter-black.png"));
            Press(Key.B);
            check(!show.Cover.IsVisible && !pv.Cover.IsVisible && pv.State.Text == "", "presenter: B again brings it back");

            Press(Key.L, ModifierKeys.Control);
            pv.PointerMoved(new Point(100, 50));
            show.UpdateLayout();
            check(main.Session.LaserAt == new Point(100, 50) && show.LaserDot.IsVisible && pv.LaserDot.IsVisible
                  && Math.Abs(System.Windows.Controls.Canvas.GetLeft(show.LaserDot) + show.LaserDot.Width / 2 - 100) < 0.01,
                "presenter: the laser on the presenter's slide shows on the show at the same place");
            pv.PointerMoved(new Point(-5, 10));
            check(main.Session.LaserAt == null && !show.LaserDot.IsVisible, "presenter: off the slide = no laser dot");
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
            check(main.InkView is { IsVisible: true } iv && iv.Ink.IsHitTestVisible && show.Zoomed, "zoom: the pen draws while zoomed");
            main.OverlayInk.Add(Line(w * 0.3, h * 0.3, w * 0.45, h * 0.35));
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
