using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace PptSlider;

/// <summary>
/// Ink on the screen: a transparent full-screen window over one monitor, above the slide show, the editor or any other app.
/// What it shows is <see cref="MainWindow.OverlayInk"/> (the current slide's strokes), so each slide brings back its own ink.
/// All state lives in the main window; this is a view.
/// </summary>
public partial class InkOverlay : Window
{
    /// <summary>Self-test: a 1280×720 window off screen that never takes focus.</summary>
    internal static bool OffScreen;

    /// <summary>Hit-testable but invisible: while drawing, the whole screen takes the mouse.</summary>
    static readonly Brush Catch = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

    readonly MainWindow main;
    readonly PenToolbar tools;
    public MonitorInfo Monitor { get; }
    /// <summary>The show this overlay sits on (its owner), or null over the desktop.</summary>
    public Window? Over { get; }
    Point? grip;

    public InkOverlay(MainWindow main, MonitorInfo monitor, Window? over)
    {
        InitializeComponent();
        this.main = main;
        Monitor = monitor;
        Over = over;
        if (OffScreen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000; Top = 2000; Width = 1280; Height = 720;
        }
        else
        {
            Monitors.PlaceFullScreen(this, monitor);
            Owner = over; // owned windows stay above their owner, even when the show is clicked
            Topmost = StaysOnTop(over);
        }
        var hooks = new WindowHooks(this);
        if (over != null)
        {
            // the one toolbar of the show: where the show's own was, with its buttons
            Bar.HorizontalAlignment = HorizontalAlignment.Left;
            Bar.VerticalAlignment = VerticalAlignment.Bottom;
            Bar.Margin = new Thickness(16);
            ShowNav.Visibility = TLaser.Visibility = TSpot.Visibility = ShowEnd.Visibility = Visibility.Visible;
            hooks.Add(() => main.Session.Changed += OnShowState, () => main.Session.Changed -= OnShowState);
            OnShowState();
            THide.Tag = ""; // "hide ink" must not look like the show's ✕ (end the show)
        }
        tools = new PenToolbar(main.Pen, TSelect, TLaser, TPen, THi, TEraser).Swatches(Swatches, 20, new Thickness(3, 0, 3, 0), Brushes.White);
        PenState.StraightenOnShift(Ink);
        Ink.StrokeCollected += (_, e) => main.TagStroke(e.Stroke, this);
        hooks.Add(() => main.Pen.Changed += OnPen, () => main.Pen.Changed -= OnPen);
        // the strokes are the main window's and outlive this window: InkCanvas listens to them, so let go of them
        hooks.OnClose(() => Ink.Strokes = new StrokeCollection());
        OnPen();
    }

    /// <summary>
    /// Over the desktop the ink goes over every app, so the overlay stays on top. Over a show it only stays above the show
    /// (its owner): another window, a browser say, can be brought in front of the show and its ink.
    /// </summary>
    internal static bool StaysOnTop(Window? over) => over == null;

    public StrokeCollection Strokes
    {
        get => Ink.Strokes;
        set { if (Ink.Strokes != value) Ink.Strokes = value; }
    }

    void OnPen()
    {
        var pen = main.Pen;
        pen.Apply(Ink);
        Ink.Background = main.Drawing ? Catch : null; // pointer mode: clicks go through to the window underneath
        tools.Sync(pen.Tool == Tool.Laser && Over == null ? Tool.Select : pen.Tool); // no laser over the editor: shown as the pointer
        SizeText.Text = pen.Size.ToString("0");
        if (!Bar.IsMouseOver) Bar.Opacity = main.Drawing ? 0.9 : 0.35;
    }

    // ---------- input: keys go to the show or the editor ----------

    void OnKey(object sender, KeyEventArgs e) => e.Handled = Press(Gesture.From(e));

    internal bool Press(Gesture g) => main.InkPress(g);

    void OnTool(object sender, RoutedEventArgs e) => tools.Pick(sender);

    void OnThinner(object sender, RoutedEventArgs e) => main.Pen.Grow(-1);
    void OnThicker(object sender, RoutedEventArgs e) => main.Pen.Grow(1);
    void OnUndo(object sender, RoutedEventArgs e) => main.UndoShowInk();
    void OnClear(object sender, RoutedEventArgs e) => main.OverlayInk.Clear();
    void OnHide(object sender, RoutedEventArgs e) => main.HideInk();
    // over a show only
    void OnPrev(object sender, RoutedEventArgs e) => main.Go(main.Current - 1);
    void OnNext(object sender, RoutedEventArgs e) => main.Go(main.Current + 1);
    void OnBreak(object sender, RoutedEventArgs e) => main.OpenBreak();
    void OnEnd(object sender, RoutedEventArgs e) => Over?.Close();
    void OnSpot(object sender, RoutedEventArgs e) => main.Session.SetSpotlight(!main.Session.Spotlight);
    void OnShowState() => TSpot.IsChecked = main.Session.Spotlight;
    void OnBarEnter(object sender, MouseEventArgs e) => Bar.Opacity = 1;
    void OnBarLeave(object sender, MouseEventArgs e) => Bar.Opacity = main.Drawing ? 0.9 : 0.35;

    // the toolbar can be dragged out of the way
    void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        grip = e.GetPosition(Root);
        Grip.CaptureMouse();
        e.Handled = true;
    }

    void OnGripMove(object sender, MouseEventArgs e)
    {
        if (grip is not { } start) return;
        var p = e.GetPosition(Root);
        BarMove.X += p.X - start.X;
        BarMove.Y += p.Y - start.Y;
        grip = p;
    }

    void OnGripUp(object sender, MouseButtonEventArgs e)
    {
        grip = null;
        Grip.ReleaseMouseCapture();
    }
}
