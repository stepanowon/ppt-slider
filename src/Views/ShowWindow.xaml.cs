using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PptSlider;

/// <summary>Full-screen slide show on the chosen monitor. Its state is the main window's <see cref="ShowSession"/>; this is a view.</summary>
public partial class ShowWindow : Window
{
    readonly MainWindow main;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer idle = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly PenToolbar tools;
    bool cursorHidden;
    Point? lastMouse;
    public MonitorInfo Monitor { get; }

    /// <param name="test">Self-test: a 1280×720 window off screen that never takes focus.</param>
    public ShowWindow(MainWindow main, MonitorInfo monitor, bool test = false)
    {
        InitializeComponent();
        this.main = main;
        this.test = test;
        Monitor = monitor;
        if (test)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = false;
            ShowActivated = false;
            ShowInTaskbar = false;
            Left = -20000; Top = 0; Width = 1280; Height = 720;
        }
        else Monitors.PlaceFullScreen(this, monitor);
        tools = new PenToolbar(main.Pen, TSelect, TLaser, TPen, THi, TEraser).Swatches(Swatches, 20, new Thickness(3, 0, 3, 0), Brushes.White);
        Stage.Width = main.Deck!.Renderer.Width;
        Stage.Height = main.Deck.Renderer.Height;
        LaserDot.Width = LaserDot.Height = laserSize = Stage.Width / 80;
        var hooks = new WindowHooks(this);
        hooks.Add(() => main.SlideChanged += Refresh, () => main.SlideChanged -= Refresh);
        hooks.Add(() => main.Pen.Changed += OnPen, () => main.Pen.Changed -= OnPen);
        hooks.Add(() => main.Session.Changed += OnShowState, () => main.Session.Changed -= OnShowState);
        hooks.Add(() => main.Session.LaserMoved += DrawLaser, () => main.Session.LaserMoved -= DrawLaser);
        hooks.OnClose(() => { clock.Stop(); idle.Stop(); FollowCursor(false); });
        clock.Tick += (_, _) => UpdateClock();
        idle.Tick += (_, _) => { idle.Stop(); cursorHidden = true; UpdateCursor(); };
        Refresh();
        OnPen();
        clock.Start();
        UpdateClock();
    }

    void Refresh()
    {
        if (main.CurrentSlide is not { } slide) { Close(); return; }
        if (Zooming) ResetZoom(); // another slide starts whole
        Host.Content = main.Deck!.Renderer.Render(slide);
        Page.Text = $"{main.Current + 1} / {main.Count}";
        OnShowState();
    }

    void OnShowState()
    {
        var s = main.Session;
        var b = s.Blanked;
        Cover.Visibility = b == Blank.None ? Visibility.Collapsed : Visibility.Visible;
        Cover.Fill = Root.Background = b == Blank.White ? Brushes.White : Brushes.Black;
        JumpBox.Visibility = s.Jump == "" ? Visibility.Collapsed : Visibility.Visible;
        JumpText.Text = L.F("slideN", s.Jump);
        TSpot.IsChecked = s.Spotlight;
        if (!s.Spotlight) SpotAt(null);
        FollowCursor(s.Spotlight && !test); // the self-test places the circle itself
    }

    // ---------- spot highlight: a circle that follows the mouse pointer ----------

    readonly bool test;
    bool following;

    /// <summary>
    /// Every frame while on: the pointer is read from Windows, not from this window's mouse events, because while
    /// drawing the ink overlay above has the mouse.
    /// </summary>
    void FollowCursor(bool on)
    {
        if (on == following) return;
        following = on;
        if (on) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var c) || PresentationSource.FromVisual(Root) == null) return;
        var p = Root.PointFromScreen(new Point(c.X, c.Y)); // physical pixels, like the per-monitor-DPI cursor position
        SpotAt(new Rect(Root.RenderSize).Contains(p) ? p : null);
    }

    /// <summary>Centers the circle on <paramref name="p"/> (on <see cref="Root"/>); null hides it (pointer off this screen).</summary>
    internal void SpotAt(Point? p)
    {
        Spot.Visibility = p != null && main.Session.Spotlight ? Visibility.Visible : Visibility.Collapsed;
        if (p is not { } at) return;
        Canvas.SetLeft(Spot, at.X - Spot.Width / 2);
        Canvas.SetTop(Spot, at.Y - Spot.Height / 2);
    }

    void OnSpot(object sender, RoutedEventArgs e) => main.Session.SetSpotlight(!main.Session.Spotlight);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

    void OnPen()
    {
        tools.Sync();
        SizeText.Text = main.Pen.Size.ToString("0");
        UpdateCursor();
        DrawLaser();
    }

    // ---------- zoom: Z, then drag the area that should fill the screen ----------

    enum ZoomMode { None, Picking, Zoomed }
    ZoomMode zoom;
    Point? pickStart; // on Root
    double laserSize;

    /// <summary>Picking an area or zoomed in; Esc or a click goes back to the whole slide.</summary>
    public bool Zooming => zoom != ZoomMode.None;
    internal bool Zoomed => zoom == ZoomMode.Zoomed;

    public void ToggleZoom()
    {
        if (Zooming) ResetZoom();
        else
        {
            if (main.Drawing) main.Pen.SetTool(Tool.Select); // the drag picks the area, it doesn't draw
            SetZoom(ZoomMode.Picking);
        }
    }

    public void ResetZoom()
    {
        Stage.RenderTransform = Transform.Identity;
        LaserDot.Width = LaserDot.Height = laserSize;
        SetZoom(ZoomMode.None);
    }

    void SetZoom(ZoomMode z)
    {
        zoom = z;
        pickStart = null;
        PickBox.Visibility = Visibility.Collapsed;
        ZoomHint.Visibility = z == ZoomMode.Picking ? Visibility.Visible : Visibility.Collapsed;
        OnPen();
        DrawLaser();
    }

    /// <summary>Scales the slide so <paramref name="r"/> (slide coordinates) fills the screen, up to 8×, never showing past the slide's edge.</summary>
    internal void ZoomTo(Rect r)
    {
        double w = Stage.Width, h = Stage.Height;
        r.Intersect(new Rect(0, 0, w, h));
        if (r.IsEmpty || r.Width < w / 50 || r.Height < h / 50) { SetZoom(ZoomMode.Picking); return; } // a click, not a drag: keep picking
        double s = Math.Min(8, Math.Min(w / r.Width, h / r.Height));
        double hw = w / (2 * s), hh = h / (2 * s);
        double cx = Math.Clamp(r.X + r.Width / 2, hw, w - hw), cy = Math.Clamp(r.Y + r.Height / 2, hh, h - hh);
        var m = Matrix.Identity;
        m.Translate(-cx, -cy);
        m.Scale(s, s);
        m.Translate(w / 2, h / 2);
        Stage.RenderTransform = new MatrixTransform(m);
        LaserDot.Width = LaserDot.Height = laserSize / s; // the dot stays its size on screen
        SetZoom(ZoomMode.Zoomed);
    }

    void OnPickDown(object sender, MouseButtonEventArgs e)
    {
        if (zoom != ZoomMode.Picking || Bar.IsMouseOver) return;
        PickDown(e.GetPosition(Root));
        Root.CaptureMouse();
        e.Handled = true;
    }

    void OnPickMove(object sender, MouseEventArgs e)
    {
        if (pickStart != null) PickMove(e.GetPosition(Root));
    }

    void OnPickUp(object sender, MouseButtonEventArgs e)
    {
        if (pickStart == null) return;
        Root.ReleaseMouseCapture();
        PickUp(e.GetPosition(Root));
        e.Handled = true;
    }

    /// <param name="p">On <see cref="Root"/>, like the other Pick methods.</param>
    internal void PickDown(Point p)
    {
        pickStart = p;
        PickMove(p);
        PickBox.Visibility = Visibility.Visible;
    }

    internal void PickMove(Point p)
    {
        var r = new Rect(pickStart!.Value, p);
        Canvas.SetLeft(PickBox, r.X);
        Canvas.SetTop(PickBox, r.Y);
        PickBox.Width = r.Width;
        PickBox.Height = r.Height;
    }

    internal void PickUp(Point p)
    {
        var a = Root.TranslatePoint(pickStart!.Value, Stage);
        ZoomTo(new Rect(a, Root.TranslatePoint(p, Stage)));
    }

    /// <summary>"12:34" or "1:02:03".</summary>
    internal static string Elapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    void UpdateClock() => Clock.Text = L.F("clock", DateTime.Now, Elapsed(DateTime.Now - main.Session.Start));

    // ---------- pointer: laser dot, cursor hidden after 3 seconds still ----------

    void UpdateCursor() => Root.Cursor = zoom == ZoomMode.Picking ? Cursors.Cross : main.Pen.Tool switch
    {
        Tool.Laser => Cursors.None,
        Tool.Select => cursorHidden ? Cursors.None : Cursors.Arrow,
        _ => null,
    };

    void OnMouseMove(object sender, MouseEventArgs e) => PointerMoved(e.GetPosition(Root));

    void OnMouseLeave(object sender, MouseEventArgs e) { lastMouse = null; main.Session.SetLaser(null); }

    /// <param name="p">Mouse position on <see cref="Root"/>.</param>
    internal void PointerMoved(Point p)
    {
        if (p == lastMouse) return; // WPF repeats MouseMove when the cursor changes: only real moves count
        lastMouse = p;
        cursorHidden = false;
        idle.Stop();
        idle.Start();
        UpdateCursor();
        if (main.Pen.Tool != Tool.Laser) return;
        var s = Root.TranslatePoint(p, Stage);
        main.Session.SetLaser(!Bar.IsMouseOver && new Rect(0, 0, Stage.Width, Stage.Height).Contains(s) ? s : null);
    }

    /// <summary>The dot follows <see cref="ShowSession.LaserAt"/>, which the presenter view's mouse can move too.</summary>
    void DrawLaser() => PlaceLaser(LaserDot, main);

    internal static void PlaceLaser(FrameworkElement dot, MainWindow main)
    {
        dot.Visibility = main.Pen.Tool == Tool.Laser && main.Session.LaserAt != null ? Visibility.Visible : Visibility.Collapsed;
        if (main.Session.LaserAt is not { } p) return;
        Canvas.SetLeft(dot, p.X - dot.Width / 2);
        Canvas.SetTop(dot, p.Y - dot.Height / 2);
    }

    // ---------- input ----------

    void OnKey(object sender, KeyEventArgs e) => e.Handled = Press(Gesture.From(e));

    /// <summary>Handles a key press; false when it did nothing.</summary>
    internal bool Press(Gesture g) => main.ShowPress(g);

    void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (main.Pen.Tool is Tool.Select or Tool.Laser && !Bar.IsMouseOver) main.ShowAdvance(1);
    }

    void OnWheel(object sender, MouseWheelEventArgs e) => main.ShowAdvance(e.Delta < 0 ? 1 : -1);

    void OnBarEnter(object sender, MouseEventArgs e) => Bar.Opacity = 1;
    void OnBarLeave(object sender, MouseEventArgs e) => Bar.Opacity = 0.25;
    void OnPrev(object sender, RoutedEventArgs e) => main.Go(main.Current - 1);
    void OnNext(object sender, RoutedEventArgs e) => main.Go(main.Current + 1);
    void OnClear(object sender, RoutedEventArgs e) => main.OverlayInk.Clear();
    void OnUndoInk(object sender, RoutedEventArgs e) => main.UndoShowInk();
    void OnThinner(object sender, RoutedEventArgs e) => main.Pen.Grow(-1);
    void OnThicker(object sender, RoutedEventArgs e) => main.Pen.Grow(1);
    void OnBreak(object sender, RoutedEventArgs e) => main.OpenBreak();
    void OnEnd(object sender, RoutedEventArgs e) => Close();

    void OnTool(object sender, RoutedEventArgs e) => tools.Pick(sender);
}
