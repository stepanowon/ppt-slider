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
        }
        finally { main.CloseForTest(); }
    }

    static Stroke Line(double x1, double y1, double x2, double y2) =>
        new(new StylusPointCollection { new StylusPoint(x1, y1), new StylusPoint(x2, y2) })
        {
            DrawingAttributes = { Color = Colors.Red, Width = 6, Height = 6 },
        };

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
