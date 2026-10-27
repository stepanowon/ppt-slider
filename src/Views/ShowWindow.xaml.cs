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
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(3, 0, 3, 0), Background = new SolidColorBrush(c), BorderBrush = Brushes.White, Focusable = false, ToolTip = "펜/형광펜 색" };
            Shortcuts.SetFor(b, $"color{i + 1}");
            b.Click += (_, _) => main.Pen.SetColor(c);
            Swatches.Children.Add(b);
        }
        Stage.Width = main.Deck!.Renderer.Width;
        Stage.Height = main.Deck.Renderer.Height;
        LaserDot.Width = LaserDot.Height = Stage.Width / 80;
        main.SlideChanged += Refresh;
        main.Pen.Changed += OnPen;
        main.ShowStateChanged += OnShowState;
        clock.Tick += (_, _) => UpdateClock();
        idle.Tick += (_, _) => { idle.Stop(); cursorHidden = true; UpdateCursor(); };
        Closed += (_, _) =>
        {
            clock.Stop();
            idle.Stop();
            main.SlideChanged -= Refresh;
            main.Pen.Changed -= OnPen;
            main.ShowStateChanged -= OnShowState;
        };
        Refresh();
        OnPen();
        clock.Start();
        UpdateClock();
    }

    void Refresh()
    {
        if (main.CurrentSlide is not { } slide) { Close(); return; }
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
        JumpText.Text = $"슬라이드 {main.Jump}";
    }

    void OnPen()
    {
        main.Pen.Apply(Ink);
        TSelect.IsChecked = main.Pen.Tool == Tool.Select;
        TLaser.IsChecked = main.Pen.Tool == Tool.Laser;
        TPen.IsChecked = main.Pen.Tool == Tool.Pen;
        THi.IsChecked = main.Pen.Tool == Tool.Highlighter;
        TEraser.IsChecked = main.Pen.Tool == Tool.Eraser;
        SizeText.Text = main.Pen.Size.ToString("0");
        UpdateCursor();
        MoveLaser(lastMouse);
    }

    /// <summary>"12:34" or "1:02:03".</summary>
    internal static string Elapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    void UpdateClock() => Clock.Text = $"{DateTime.Now:t}   경과 {Elapsed(DateTime.Now - main.ShowStart)}";

    // ---------- pointer: laser dot, cursor hidden after 3 seconds still ----------

    void UpdateCursor() => Root.Cursor = main.Pen.Tool switch
    {
        Tool.Laser => Cursors.None,
        Tool.Select => cursorHidden ? Cursors.None : Cursors.Arrow,
        _ => null, // the ink canvas' own pen / eraser cursor
    };

    void OnMouseMove(object sender, MouseEventArgs e) => PointerMoved(e.GetPosition(Root));

    void OnMouseLeave(object sender, MouseEventArgs e) { lastMouse = null; MoveLaser(null); }

    /// <param name="p">Mouse position on <see cref="Root"/>.</param>
    internal void PointerMoved(Point p)
    {
        if (p == lastMouse) return; // WPF repeats MouseMove when the cursor changes: only real moves count
        lastMouse = p;
        cursorHidden = false;
        idle.Stop();
        idle.Start();
        UpdateCursor();
        MoveLaser(p);
    }

    void MoveLaser(Point? onRoot)
    {
        var s = onRoot is { } p ? Root.TranslatePoint(p, Stage) : default;
        bool on = main.Pen.Tool == Tool.Laser && onRoot != null && !Bar.IsMouseOver
                  && s.X >= 0 && s.Y >= 0 && s.X <= Stage.Width && s.Y <= Stage.Height;
        LaserDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(LaserDot, s.X - LaserDot.Width / 2);
        Canvas.SetTop(LaserDot, s.Y - LaserDot.Height / 2);
    }

    // ---------- input ----------

    void OnKey(object sender, KeyEventArgs e) => e.Handled = Press(Gesture.From(e));

    /// <summary>Handles a key press; false when it did nothing.</summary>
    internal bool Press(Gesture g)
    {
        if (main.ShowKey(g)) return true;
        if (g is not { Key: Key.Escape, Mods: ModifierKeys.None }) return false;
        if (main.Pen.Tool != Tool.Select) main.Pen.SetTool(Tool.Select);
        else Close();
        return true;
    }

    bool Pointing => main.Pen.Tool is Tool.Select or Tool.Laser;

    void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (!Pointing || Bar.IsMouseOver) return;
        if (main.Blanked != Blank.None) main.SetBlank(Blank.None);
        else main.Go(main.Current + 1); // click advances like PowerPoint
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (main.Blanked != Blank.None) main.SetBlank(Blank.None);
        else main.Go(main.Current + (e.Delta < 0 ? 1 : -1));
    }

    void OnBarEnter(object sender, MouseEventArgs e) => Bar.Opacity = 1;
    void OnBarLeave(object sender, MouseEventArgs e) => Bar.Opacity = 0.25;
    void OnPrev(object sender, RoutedEventArgs e) => main.Go(main.Current - 1);
    void OnNext(object sender, RoutedEventArgs e) => main.Go(main.Current + 1);
    void OnClear(object sender, RoutedEventArgs e) => main.ShowInk.Clear();
    void OnThinner(object sender, RoutedEventArgs e) => main.Pen.Grow(-1);
    void OnThicker(object sender, RoutedEventArgs e) => main.Pen.Grow(1);
    void OnBreak(object sender, RoutedEventArgs e) => main.OpenBreak();
    void OnEnd(object sender, RoutedEventArgs e) => Close();

    void OnTool(object sender, RoutedEventArgs e) =>
        main.Pen.SetTool(sender == TPen ? Tool.Pen : sender == THi ? Tool.Highlighter : sender == TEraser ? Tool.Eraser : sender == TLaser ? Tool.Laser : Tool.Select);
}
