using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PptSlider;

/// <summary>
/// `PptSlider.exe --screenshots deck.pptx outDir [ko|ja|en]`: Store screenshots of the real windows on the primary monitor
/// (editor, screen ink over the editor, slide show with ink, presenter view). Captures the screen, so it needs a free desktop.
/// </summary>
static partial class SelfTest
{
    public static int Screens(string file, string outDir, int slide)
    {
        Directory.CreateDirectory(outDir);
        var all = Monitors.All();
        var primary = all.First(m => m.Primary);
        var wa = SystemParameters.WorkArea; // the editor without the taskbar (the primary monitor is at 100%)
        var work = new Int32Rect((int)wa.X, (int)wa.Y, (int)wa.Width, (int)wa.Height);
        var main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = primary.Bounds.X + 40, Top = primary.Bounds.Y + 40, Topmost = true };
        main.Show();
        main.WindowState = WindowState.Maximized;
        try
        {
            main.Open(file);
            main.Go(Math.Min(slide, main.Count - 1));
            Wait(800);
            Capture(work, Path.Combine(outDir, "1-editor.png"));

            // screen ink over the editor
            main.Ribbon.SelectedIndex = 1;
            main.Run("pen");
            while (main.Pen.Size < 4) main.Pen.Grow(1);
            Wait(300);
            Draw(main, main.Stage);
            main.InkView!.Activate();
            Wait(500);
            Capture(work, Path.Combine(outDir, "2-editor-ink.png"));
            main.Run("select");

            // the show on the primary monitor, with its ink
            var monitors = (IEnumerable<MonitorInfo>)main.MonitorBox.ItemsSource;
            main.MonitorBox.SelectedItem = monitors.First(m => m.Primary);
            main.PresenterBox.IsChecked = false;
            main.StartShow(main.Current);
            Wait(600);
            main.Run("pen");
            Wait(300);
            main.OverlayInk.Clear();
            main.InkView!.BarMove.X = 560; // the toolbar moved off the title, as a presenter would
            Draw(main, main.ShowView!.Stage);
            Wait(500);
            Capture(primary.Bounds, Path.Combine(outDir, "3-show-ink.png"));
            main.Run("select");
            main.ShowView?.Close();
            Wait(400);

            // presenter view on the primary monitor, the show on another one
            if (monitors.FirstOrDefault(m => !m.Primary) is { } other)
            {
                main.Topmost = false;
                main.MonitorBox.SelectedItem = other;
                main.PresenterBox.IsChecked = true;
                main.StartShow(main.Current);
                Wait(1500);
                Capture(primary.Bounds, Path.Combine(outDir, "4-presenter.png"));
                main.ShowView?.Close();
                Wait(300);
            }
            main.OverlayInk.Clear();
            return 0;
        }
        finally { main.CloseForTest(); main.Close(); }
    }

    /// <summary>Some handwriting on the slide (slide coordinates, 1280×720 design): a circle, an arrow, a highlight, a check.</summary>
    static void Draw(MainWindow main, FrameworkElement stage)
    {
        var ov = main.InkView!;
        double sx = stage.Width / 1280, sy = stage.Height / 720;
        void Add(IEnumerable<Point> pts, Color c, double w, bool hi = false)
        {
            var on = new StylusPointCollection(pts.Select(p => ov.PointFromScreen(stage.PointToScreen(new Point(p.X * sx, p.Y * sy)))).Select(p => new StylusPoint(p.X, p.Y)));
            var s = new Stroke(on) { DrawingAttributes = { Color = c, Width = hi ? w * 0.4 : w, Height = w, IsHighlighter = hi, StylusTip = hi ? StylusTip.Rectangle : StylusTip.Ellipse, FitToCurve = true } };
            main.TagStroke(s, ov);
            main.OverlayInk.Add(s);
        }
        IEnumerable<Point> Range(int n, Func<double, Point> f) => Enumerable.Range(0, n + 1).Select(i => f((double)i / n));
        var red = Color.FromRgb(0xE5, 0x1C, 0x23);
        var blue = Color.FromRgb(0x1E, 0x63, 0xD6);
        // a slightly wobbly ellipse around "git add ."
        Add(Range(60, t => new Point(170 + 78 * Math.Cos(t * 2.15 * Math.PI) + 3 * Math.Sin(t * 9), 227 + 25 * Math.Sin(t * 2.15 * Math.PI))), red, 4);
        // highlight "git commit -m [커밋메시지]"
        Add(Range(10, t => new Point(88 + 350 * t, 304 + 1.5 * Math.Sin(t * 6))), Colors.Yellow, 34, hi: true);
        // an arrow from the working directory round to the repository
        Add(Range(50, t => new Point(725 + 435 * t, 352 + 48 * Math.Sin(t * Math.PI))), red, 4);
        Add([new(1135, 380), new(1162, 352), new(1166, 386)], red, 4);
        // a check next to "git log"
        Add([new(235, 650), new(250, 668), new(282, 628)], blue, 5);
        // underline the diagram's middle box label
        Add(Range(12, t => new Point(905 + 82 * t, 266 + 2 * Math.Sin(t * 5))), blue, 4);
    }

    static void Capture(Int32Rect r, string path)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, r.Width, r.Height);
        var old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.X, r.Y, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT (layered windows too)
        SelectObject(mem, old);
        var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using (var f = File.Create(path)) enc.Save(f);
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
}
