using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PptSlider;

/// <summary>What covers the slide in the show: B / W like PowerPoint.</summary>
public enum Blank { None, Black, White }

/// <summary>Full-screen slide show on the chosen monitor. All state lives in the main window; this is a view.</summary>
public partial class ShowWindow : Window
{
    readonly MainWindow main;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer idle = new() { Interval = TimeSpan.FromSeconds(3) };
    bool cursorHidden;
    Point? lastMouse;
    public MonitorInfo Monitor { get; }

    /// <param name="test">Self-test: a 1280×720 window off screen that never takes focus.</param>
    public ShowWindow(MainWindow main, MonitorInfo monitor, bool test = false)
    {
        InitializeComponent();
        this.main = main;
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
        foreach (var (c, i) in PenState.Palette.Select((c, i) => (c, i)))
        {
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(3, 0, 3, 0), Background = new SolidColorBrush(c), BorderBrush = Brushes.White, Focusable = false, ToolTip = L.T("penColor") };
            Shortcuts.SetFor(b, $"color{i + 1}");
            b.Click += (_, _) => main.Pen.SetColor(c);
            Swatches.Children.Add(b);
        }
        Stage.Width = main.Deck!.Renderer.Width;
        Stage.Height = main.Deck.Renderer.Height;
        LaserDot.Width = LaserDot.Height = laserSize = Stage.Width / 80;
        PenState.StraightenOnShift(Ink);
        main.SlideChanged += Refresh;
        main.Pen.Changed += OnPen;
        main.ShowStateChanged += OnShowState;
        main.LaserMoved += DrawLaser;
        clock.Tick += (_, _) => UpdateClock();
        idle.Tick += (_, _) => { idle.Stop(); cursorHidden = true; UpdateCursor(); };
        Closed += (_, _) =>
        {
            clock.Stop();
            idle.Stop();
            main.SlideChanged -= Refresh;
            main.Pen.Changed -= OnPen;
            main.ShowStateChanged -= OnShowState;
            main.LaserMoved -= DrawLaser;
        };
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
        var b = main.Blanked;
        Cover.Visibility = b == Blank.None ? Visibility.Collapsed : Visibility.Visible;
        Cover.Fill = Root.Background = b == Blank.White ? Brushes.White : Brushes.Black;
        Ink.Strokes = main.ShowInk; // the slide's: the same collection as the editor, so ink shows in both
        JumpBox.Visibility = main.Jump == "" ? Visibility.Collapsed : Visibility.Visible;
        JumpText.Text = L.F("slideN", main.Jump);
    }

    void OnPen()
    {
        main.Pen.Apply(Ink);
        if (zoom == ZoomMode.Picking) Ink.IsHitTestVisible = false; // the drag picks the area, it doesn't draw
        TSelect.IsChecked = main.Pen.Tool == Tool.Select;
        TLaser.IsChecked = main.Pen.Tool == Tool.Laser;
        TPen.IsChecked = main.Pen.Tool == Tool.Pen;
        THi.IsChecked = main.Pen.Tool == Tool.Highlighter;
        TEraser.IsChecked = main.Pen.Tool == Tool.Eraser;
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
        else SetZoom(ZoomMode.Picking);
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

    void UpdateClock() => Clock.Text = L.F("clock", DateTime.Now, Elapsed(DateTime.Now - main.ShowStart));

    // ---------- pointer: laser dot, cursor hidden after 3 seconds still ----------

    void UpdateCursor() => Root.Cursor = zoom == ZoomMode.Picking ? Cursors.Cross : main.Pen.Tool switch
    {
        Tool.Laser => Cursors.None,
        Tool.Select => cursorHidden ? Cursors.None : Cursors.Arrow,
        _ => null, // the ink canvas' own pen / eraser cursor
    };

    void OnMouseMove(object sender, MouseEventArgs e) => PointerMoved(e.GetPosition(Root));

    void OnMouseLeave(object sender, MouseEventArgs e) { lastMouse = null; main.SetLaser(null); }

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
        main.SetLaser(!Bar.IsMouseOver && new Rect(0, 0, Stage.Width, Stage.Height).Contains(s) ? s : null);
    }

    /// <summary>The dot follows <see cref="MainWindow.LaserAt"/>, which the presenter view's mouse can move too.</summary>
    void DrawLaser() => PlaceLaser(LaserDot, main);

    internal static void PlaceLaser(FrameworkElement dot, MainWindow main)
    {
        dot.Visibility = main.Pen.Tool == Tool.Laser && main.LaserAt != null ? Visibility.Visible : Visibility.Collapsed;
        if (main.LaserAt is not { } p) return;
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
    void OnClear(object sender, RoutedEventArgs e) => main.ShowInk.Clear();
    void OnUndoInk(object sender, RoutedEventArgs e) => main.UndoShowInk();
    void OnThinner(object sender, RoutedEventArgs e) => main.Pen.Grow(-1);
    void OnThicker(object sender, RoutedEventArgs e) => main.Pen.Grow(1);
    void OnBreak(object sender, RoutedEventArgs e) => main.OpenBreak();
    void OnEnd(object sender, RoutedEventArgs e) => Close();

    void OnTool(object sender, RoutedEventArgs e) =>
        main.Pen.SetTool(sender == TPen ? Tool.Pen : sender == THi ? Tool.Highlighter : sender == TEraser ? Tool.Eraser : sender == TLaser ? Tool.Laser : Tool.Select);
}
