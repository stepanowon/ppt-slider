using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PptSlider;

/// <summary>Full-screen slide show on the chosen monitor. All state lives in the main window; this is a view.</summary>
public partial class ShowWindow : Window
{
    readonly MainWindow main;
    public MonitorInfo Monitor { get; }

    public ShowWindow(MainWindow main, MonitorInfo monitor)
    {
        InitializeComponent();
        this.main = main;
        Monitor = monitor;
        Monitors.PlaceFullScreen(this, monitor);
        foreach (var c in PenState.Palette)
        {
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(3, 0, 3, 0), Background = new SolidColorBrush(c), BorderBrush = Brushes.White, Focusable = false };
            b.Click += (_, _) => main.Pen.SetColor(c);
            Swatches.Children.Add(b);
        }
        Stage.Width = main.Deck!.Renderer.Width;
        Stage.Height = main.Deck.Renderer.Height;
        main.SlideChanged += Refresh;
        main.Pen.Changed += OnPen;
        Closed += (_, _) => { main.SlideChanged -= Refresh; main.Pen.Changed -= OnPen; };
        Refresh();
        OnPen();
    }

    void Refresh()
    {
        if (main.CurrentSlide is not { } slide) { Close(); return; }
        Host.Content = main.Deck!.Renderer.Render(slide);
        Ink.Strokes = main.StrokesOf(slide); // same collection as the editor: ink shows in both
        Page.Text = $"{main.Current + 1} / {main.Count}";
    }

    void OnPen()
    {
        main.Pen.Apply(Ink);
        TSelect.IsChecked = main.Pen.Tool == Tool.Select;
        TPen.IsChecked = main.Pen.Tool == Tool.Pen;
        THi.IsChecked = main.Pen.Tool == Tool.Highlighter;
        TEraser.IsChecked = main.Pen.Tool == Tool.Eraser;
        SizeText.Text = main.Pen.Size.ToString("0");
        Cursor = main.Pen.Tool == Tool.Select ? Cursors.Arrow : null;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var k = MainWindow.RealKey(e);
        e.Handled = true;
        if (k == Key.Escape)
        {
            if (main.Pen.Tool != Tool.Select) main.Pen.SetTool(Tool.Select);
            else Close();
        }
        else e.Handled = main.HandleCommonKey(k);
    }

    void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (main.Pen.Tool == Tool.Select && !Bar.IsMouseOver) main.Go(main.Current + 1); // pointer mode: click advances like PowerPoint
    }

    void OnWheel(object sender, MouseWheelEventArgs e) => main.Go(main.Current + (e.Delta < 0 ? 1 : -1));
    void OnBarEnter(object sender, MouseEventArgs e) => Bar.Opacity = 1;
    void OnBarLeave(object sender, MouseEventArgs e) => Bar.Opacity = 0.25;
    void OnPrev(object sender, RoutedEventArgs e) => main.Go(main.Current - 1);
    void OnNext(object sender, RoutedEventArgs e) => main.Go(main.Current + 1);
    void OnClear(object sender, RoutedEventArgs e) => main.ClearInk();
    void OnThinner(object sender, RoutedEventArgs e) => main.Pen.Grow(-1);
    void OnThicker(object sender, RoutedEventArgs e) => main.Pen.Grow(1);
    void OnBreak(object sender, RoutedEventArgs e) => main.OpenBreak();
    void OnEnd(object sender, RoutedEventArgs e) => Close();

    void OnTool(object sender, RoutedEventArgs e) =>
        main.Pen.SetTool(sender == TPen ? Tool.Pen : sender == THi ? Tool.Highlighter : sender == TEraser ? Tool.Eraser : Tool.Select);
}
