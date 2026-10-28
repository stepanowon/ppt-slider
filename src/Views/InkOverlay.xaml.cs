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
            Topmost = false;
            Left = -20000; Top = 2000; Width = 1280; Height = 720;
        }
        else
        {
            Monitors.PlaceFullScreen(this, monitor);
            Owner = over; // owned windows stay above their owner, even when the show is clicked
        }
        foreach (var (c, i) in PenState.Palette.Select((c, i) => (c, i)))
        {
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(3, 0, 3, 0), Background = new SolidColorBrush(c), BorderBrush = Brushes.White, Focusable = false, ToolTip = L.T("penColor") };
            Shortcuts.SetFor(b, $"color{i + 1}");
            b.Click += (_, _) => main.Pen.SetColor(c);
            Swatches.Children.Add(b);
        }
        PenState.StraightenOnShift(Ink);
        Ink.StrokeCollected += (_, e) => main.TagStroke(e.Stroke, this);
        main.Pen.Changed += OnPen;
        Closed += (_, _) => main.Pen.Changed -= OnPen;
        OnPen();
    }

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
        TSelect.IsChecked = pen.Tool is Tool.Select or Tool.Laser;
        TPen.IsChecked = pen.Tool == Tool.Pen;
        THi.IsChecked = pen.Tool == Tool.Highlighter;
        TEraser.IsChecked = pen.Tool == Tool.Eraser;
        SizeText.Text = pen.Size.ToString("0");
        if (!Bar.IsMouseOver) Bar.Opacity = main.Drawing ? 0.9 : 0.35;
    }

    // ---------- input: keys go to the show or the editor ----------

    void OnKey(object sender, KeyEventArgs e) => e.Handled = Press(Gesture.From(e));

    internal bool Press(Gesture g) => main.InkPress(g);

    void OnTool(object sender, RoutedEventArgs e) =>
        main.Pen.SetTool(sender == TPen ? Tool.Pen : sender == THi ? Tool.Highlighter : sender == TEraser ? Tool.Eraser : Tool.Select);

    void OnThinner(object sender, RoutedEventArgs e) => main.Pen.Grow(-1);
    void OnThicker(object sender, RoutedEventArgs e) => main.Pen.Grow(1);
    void OnUndo(object sender, RoutedEventArgs e) => main.UndoShowInk();
    void OnClear(object sender, RoutedEventArgs e) => main.OverlayInk.Clear();
    void OnHide(object sender, RoutedEventArgs e) => main.HideInk();
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
