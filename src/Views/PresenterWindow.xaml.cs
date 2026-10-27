using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PptSlider;

/// <summary>
/// The presenter's own screen during a show: current slide (with the show's ink), next slide, speaker notes and the time.
/// Like <see cref="ShowWindow"/> it is only a view: keys, ink and the laser go through the main window.
/// </summary>
public partial class PresenterWindow : Window
{
    readonly MainWindow main;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <param name="test">Self-test: a 1280×800 window off screen that never takes focus.</param>
    public PresenterWindow(MainWindow main, MonitorInfo monitor, bool test = false)
    {
        InitializeComponent();
        this.main = main;
        if (test)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = false;
            ShowInTaskbar = false;
            Left = -20000; Top = 1000; Width = 1280; Height = 800;
        }
        else Monitors.PlaceFullScreen(this, monitor);
        foreach (var (c, i) in PenState.Palette.Select((c, i) => (c, i)))
        {
            var b = new Button { Width = 18, Height = 18, Margin = new Thickness(2, 0, 2, 0), Background = new SolidColorBrush(c), BorderBrush = Brushes.White, Focusable = false, ToolTip = "펜/형광펜 색" };
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
        main.LaserMoved += DrawLaser;
        clock.Tick += (_, _) => UpdateClock();
        Closed += (_, _) =>
        {
            clock.Stop();
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
        if (main.CurrentSlide is not { } slide) return;
        var deck = main.Deck!;
        Host.Content = deck.Renderer.Render(slide);
        bool last = main.Current >= main.Count - 1;
        NextHost.Content = last ? null : deck.Renderer.Render(deck.Slides[main.Current + 1]);
        NextHost.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        NextEnd.Visibility = last ? Visibility.Visible : Visibility.Collapsed;
        var notes = Deck.Notes(slide);
        Notes.Text = notes == "" ? "(노트 없음)" : notes;
        Notes.Foreground = notes == "" ? Brushes.Gray : Brushes.White;
        Page.Text = $"{main.Current + 1} / {main.Count}";
        OnShowState();
    }

    void OnShowState()
    {
        var b = main.Blanked;
        Cover.Visibility = b == Blank.None ? Visibility.Collapsed : Visibility.Visible;
        Cover.Fill = b == Blank.White ? Brushes.White : Brushes.Black;
        TBlack.IsChecked = b == Blank.Black;
        TWhite.IsChecked = b == Blank.White;
        Ink.Strokes = main.ShowInk;
        State.Text = main.Jump != "" ? $"슬라이드 {main.Jump} 로 이동 (Enter)"
            : b == Blank.Black ? "청중 화면: 검은 화면"
            : b == Blank.White ? "청중 화면: 흰 화면"
            : "";
    }

    void OnPen()
    {
        main.Pen.Apply(Ink);
        TSelect.IsChecked = main.Pen.Tool == Tool.Select;
        TLaser.IsChecked = main.Pen.Tool == Tool.Laser;
        TPen.IsChecked = main.Pen.Tool == Tool.Pen;
        THi.IsChecked = main.Pen.Tool == Tool.Highlighter;
        TEraser.IsChecked = main.Pen.Tool == Tool.Eraser;
        Stage.Cursor = main.Pen.Tool == Tool.Laser ? Cursors.None : null;
        DrawLaser();
    }

    void UpdateClock()
    {
        Elapsed.Text = ShowWindow.Elapsed(DateTime.Now - main.ShowStart);
        Clock.Text = DateTime.Now.ToString("t");
    }

    void DrawLaser() => ShowWindow.PlaceLaser(LaserDot, main);

    // ---------- input: the show's own handlers ----------

    void OnKey(object sender, KeyEventArgs e) => e.Handled = Press(Gesture.From(e));

    internal bool Press(Gesture g) => main.ShowPress(g);

    /// <param name="p">Mouse position on the current slide, in slide coordinates.</param>
    internal void PointerMoved(Point p)
    {
        if (main.Pen.Tool == Tool.Laser) main.SetLaser(new Rect(0, 0, Stage.Width, Stage.Height).Contains(p) ? p : null);
    }

    void OnStageMove(object sender, MouseEventArgs e) => PointerMoved(e.GetPosition(Stage));
    void OnStageLeave(object sender, MouseEventArgs e) { if (main.Pen.Tool == Tool.Laser) main.SetLaser(null); }

    void OnStageClick(object sender, MouseButtonEventArgs e)
    {
        if (main.Pen.Tool is Tool.Select or Tool.Laser) main.ShowAdvance(1);
    }

    void OnWheel(object sender, MouseWheelEventArgs e) => main.ShowAdvance(e.Delta < 0 ? 1 : -1);

    void OnPrev(object sender, RoutedEventArgs e) => main.Go(main.Current - 1);
    void OnNext(object sender, RoutedEventArgs e) => main.Go(main.Current + 1);
    void OnClear(object sender, RoutedEventArgs e) => main.ShowInk.Clear();
    void OnBlack(object sender, RoutedEventArgs e) => main.SetBlank(main.Blanked == Blank.Black ? Blank.None : Blank.Black);
    void OnWhite(object sender, RoutedEventArgs e) => main.SetBlank(main.Blanked == Blank.White ? Blank.None : Blank.White);
    void OnBreak(object sender, RoutedEventArgs e) => main.OpenBreak();
    void OnEnd(object sender, RoutedEventArgs e) => main.ShowView?.Close();
    void OnNotesBigger(object sender, RoutedEventArgs e) => SetNotesSize(Notes.FontSize + 2);
    void OnNotesSmaller(object sender, RoutedEventArgs e) => SetNotesSize(Notes.FontSize - 2);

    void SetNotesSize(double s)
    {
        Notes.FontSize = Math.Clamp(s, 12, 48);
        Notes.LineHeight = Notes.FontSize * 1.45;
    }

    void OnTool(object sender, RoutedEventArgs e) =>
        main.Pen.SetTool(sender == TPen ? Tool.Pen : sender == THi ? Tool.Highlighter : sender == TEraser ? Tool.Eraser : sender == TLaser ? Tool.Laser : Tool.Select);
}
