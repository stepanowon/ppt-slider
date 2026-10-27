using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PptSlider;

/// <summary>Full-screen break countdown, 5 minutes to 2 hours in 5-minute steps.</summary>
public partial class BreakWindow : Window
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    DateTime end;
    TimeSpan? paused;
    bool done;

    public BreakWindow(MonitorInfo monitor)
    {
        InitializeComponent();
        Monitors.PlaceFullScreen(this, monitor);
        timer.Tick += (_, _) => Tick();
        Closed += (_, _) => timer.Stop();
        OnMinutes(null, null);
    }

    void Step(int dir) => Minutes.Value = Math.Clamp(Minutes.Value + dir * 5, 5, 120);
    void OnMinus(object sender, RoutedEventArgs e) => Step(-1);
    void OnPlus(object sender, RoutedEventArgs e) => Step(1);

    void OnMinutes(object? sender, RoutedPropertyChangedEventArgs<double>? e)
    {
        if (MinutesText == null) return;
        int m = (int)Minutes.Value;
        MinutesText.Text = m < 60 ? $"{m}분" : m % 60 == 0 ? $"{m / 60}시간" : $"{m / 60}시간 {m % 60}분";
    }

    void OnStart(object? sender, RoutedEventArgs? e)
    {
        end = DateTime.Now.AddMinutes(Minutes.Value);
        Until.Text = $"{end:HH:mm}에 다시 시작합니다";
        Setup.Visibility = Visibility.Collapsed;
        Running.Visibility = Visibility.Visible;
        timer.Start();
        Tick();
    }

    void Tick()
    {
        var left = paused ?? end - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
            if (!done)
            {
                done = true;
                timer.Stop();
                Heading.Text = "휴식 끝! 수업을 시작합니다";
                Until.Text = "";
                Root.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x3E, 0x1C));
                SystemSounds.Exclamation.Play();
            }
        }
        var s = (int)Math.Ceiling(left.TotalSeconds);
        Remain.Text = s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
        Remain.Foreground = !done && s <= 60 ? Brushes.Orange : Brushes.White;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var k = MainWindow.RealKey(e);
        if (k == Key.Escape) { Close(); return; }
        if (Setup.Visibility == Visibility.Visible)
        {
            switch (k)
            {
                case Key.Right or Key.Up or Key.OemPlus or Key.Add: Step(1); break;
                case Key.Left or Key.Down or Key.OemMinus or Key.Subtract: Step(-1); break;
                case Key.Enter or Key.Space: OnStart(null, null); break;
            }
        }
        else if (k == Key.Space && !done)
        {
            if (paused is { } p) { end = DateTime.Now + p; paused = null; Until.Text = $"{end:HH:mm}에 다시 시작합니다"; }
            else { paused = end - DateTime.Now; Until.Text = "일시 정지됨"; }
            Tick();
        }
    }
}
