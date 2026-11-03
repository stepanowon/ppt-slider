using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PptSlider;

/// <summary>
/// The presenter's own screen during a show: current slide, next slide, speaker notes and the time.
/// Like <see cref="ShowWindow"/> it is only a view: keys, ink tools and the laser go through the main window
/// (the pen draws on the show's screen, see <see cref="InkOverlay"/>).
/// </summary>
public partial class PresenterWindow : Window
{
    readonly MainWindow main;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly PenToolbar tools;

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
        tools = new PenToolbar(main.Pen, TSelect, TLaser, TPen, THi, TEraser).Swatches(Swatches, 18, new Thickness(2, 0, 2, 0), Brushes.White);
        Stage.Width = main.Deck!.Renderer.Width;
        Stage.Height = main.Deck.Renderer.Height;
        LaserDot.Width = LaserDot.Height = Stage.Width / 80;
        var hooks = new WindowHooks(this);
        hooks.Add(() => main.SlideChanged += Refresh, () => main.SlideChanged -= Refresh);
        hooks.Add(() => main.Pen.Changed += OnPen, () => main.Pen.Changed -= OnPen);
        hooks.Add(() => main.Session.Changed += OnShowState, () => main.Session.Changed -= OnShowState);
        hooks.Add(() => main.Session.LaserMoved += DrawLaser, () => main.Session.LaserMoved -= DrawLaser);
        hooks.OnClose(clock.Stop);
        clock.Tick += (_, _) => UpdateClock();
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
        var notes = TextContent.Notes(slide);
        Notes.Text = notes == "" ? L.T("noNotes") : notes;
        Notes.Foreground = notes == "" ? Brushes.Gray : Brushes.White;
        Page.Text = $"{main.Current + 1} / {main.Count}";
        OnShowState();
    }

    void OnShowState()
    {
        var b = main.Session.Blanked;
        Cover.Visibility = b == Blank.None ? Visibility.Collapsed : Visibility.Visible;
        Cover.Fill = b == Blank.White ? Brushes.White : Brushes.Black;
        TBlack.IsChecked = b == Blank.Black;
        TWhite.IsChecked = b == Blank.White;
        State.Text = main.Session.Jump != "" ? L.F("goToSlide", main.Session.Jump)
            : b == Blank.Black ? L.T("audienceBlack")
            : b == Blank.White ? L.T("audienceWhite")
            : "";
    }

    void OnPen()
    {
        tools.Sync();
        Stage.Cursor = main.Pen.Tool == Tool.Laser ? Cursors.None : null;
        DrawLaser();
    }

    void UpdateClock()
    {
        Elapsed.Text = ShowWindow.Elapsed(DateTime.Now - main.Session.Start);
        Clock.Text = DateTime.Now.ToString("t");
    }

    void DrawLaser() => ShowWindow.PlaceLaser(LaserDot, main);

    // ---------- input: the show's own handlers ----------

    void OnKey(object sender, KeyEventArgs e) => e.Handled = Press(Gesture.From(e));

    internal bool Press(Gesture g) => main.ShowPress(g);

    /// <param name="p">Mouse position on the current slide, in slide coordinates.</param>
    internal void PointerMoved(Point p)
    {
        if (main.Pen.Tool == Tool.Laser) main.Session.SetLaser(new Rect(0, 0, Stage.Width, Stage.Height).Contains(p) ? p : null);
    }

    void OnStageMove(object sender, MouseEventArgs e) => PointerMoved(e.GetPosition(Stage));
    void OnStageLeave(object sender, MouseEventArgs e) { if (main.Pen.Tool == Tool.Laser) main.Session.SetLaser(null); }

    void OnStageClick(object sender, MouseButtonEventArgs e)
    {
        if (main.Pen.Tool is Tool.Select or Tool.Laser) main.ShowAdvance(1);
    }

    void OnWheel(object sender, MouseWheelEventArgs e) => main.ShowAdvance(e.Delta < 0 ? 1 : -1);

    void OnPrev(object sender, RoutedEventArgs e) => main.Go(main.Current - 1);
    void OnNext(object sender, RoutedEventArgs e) => main.Go(main.Current + 1);
    void OnClear(object sender, RoutedEventArgs e) => main.OverlayInk.Clear();
    void OnUndoInk(object sender, RoutedEventArgs e) => main.UndoShowInk();
    void OnBlack(object sender, RoutedEventArgs e) => main.Session.ToggleBlank(Blank.Black);
    void OnWhite(object sender, RoutedEventArgs e) => main.Session.ToggleBlank(Blank.White);
    void OnBreak(object sender, RoutedEventArgs e) => main.OpenBreak();
    void OnEnd(object sender, RoutedEventArgs e) => main.ShowView?.Close();
    void OnNotesBigger(object sender, RoutedEventArgs e) => SetNotesSize(Notes.FontSize + 2);
    void OnNotesSmaller(object sender, RoutedEventArgs e) => SetNotesSize(Notes.FontSize - 2);

    void SetNotesSize(double s)
    {
        Notes.FontSize = Math.Clamp(s, 12, 48);
        Notes.LineHeight = Notes.FontSize * 1.45;
    }

    void OnTool(object sender, RoutedEventArgs e) => tools.Pick(sender);
}
