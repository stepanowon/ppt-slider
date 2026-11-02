using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Win32;

namespace PptSlider;

/// <summary>Slide show, presenter view, monitors, break timer, and the show's keys and state.</summary>
public partial class MainWindow
{
    // ---------- slide show, monitors, break timer ----------

    void OnRefreshMonitors(object? sender, RoutedEventArgs? e)
    {
        var all = Monitors.All();
        MonitorBox.ItemsSource = all;
        MonitorBox.SelectedItem = all.LastOrDefault(m => !m.Primary) ?? all.FirstOrDefault(); // projector by default
    }

    MonitorInfo SelectedMonitor => MonitorBox.SelectedItem as MonitorInfo ?? Monitors.All()[0];

    internal ShowWindow? ShowView => show;
    internal PresenterWindow? PresenterView => presenter;
    PresenterWindow? presenter;

    /// <param name="test">Self-test: small windows off screen instead of full screen, the presenter view even with one monitor.</param>
    internal void StartShow(int from, bool test = false)
    {
        if (Deck == null) return;
        CommitEdit();
        show?.Close();
        Go(from);
        ShowStart = DateTime.Now;
        var shown = SelectedMonitor;
        show = new ShowWindow(this, shown, test);
        show.Closed += (_, _) =>
        {
            show = null;
            presenter?.Close();
            SetJump("");
            SetBlank(Blank.None);
            SetLaser(null);
            if (Pen.Tool == Tool.Laser) Pen.SetTool(Tool.Select);
            UpdateOverlay(); // back over the editor
            Activate();
        };
        show.Show();
        UpdateOverlay(); // over the show now
        // the presenter view goes on another monitor (the laptop screen, usually the primary one)
        var other = test ? shown : Monitors.All().Where(m => m.Bounds != shown.Bounds).OrderByDescending(m => m.Primary).FirstOrDefault();
        if (PresenterBox.IsChecked == true && other != null)
        {
            presenter = new PresenterWindow(this, other, test);
            presenter.Closed += (_, _) => presenter = null;
            presenter.Show();
        }
    }

    void OnShowFirst(object sender, RoutedEventArgs e) => StartShow(0);
    void OnShowCurrent(object sender, RoutedEventArgs e) => StartShow(Current);

    public void OpenBreak()
    {
        var w = new BreakWindow(show?.Monitor ?? SelectedMonitor) { Owner = show ?? (Window)this };
        w.Show();
    }

    void OnBreak(object sender, RoutedEventArgs e) => OpenBreak();


    // ---------- slide show keys and state (shared by the show window and the presenter view) ----------

    public Blank Blanked { get; private set; }
    /// <summary>Ink drawn on the black / white screen; a new blank screen starts empty.</summary>
    public StrokeCollection BlankInk { get; private set; } = new();
    /// <summary>The slide number being typed in the show ("" when none).</summary>
    public string Jump { get; private set; } = "";
    public DateTime ShowStart { get; private set; }
    /// <summary>Raised when the blank screen or the typed slide number changes.</summary>
    public event Action? ShowStateChanged;
    readonly DispatcherTimer jumpTimer = new();
    Gesture? jumpKey; // a lone digit: runs its own command (a pen color) if no Enter / second digit follows

    /// <summary>The laser dot in slide coordinates, null when the mouse is off the slide; set by the show or the presenter view.</summary>
    public Point? LaserAt { get; private set; }
    public event Action? LaserMoved;

    public void SetLaser(Point? p)
    {
        if (p == LaserAt) return;
        LaserAt = p;
        LaserMoved?.Invoke();
    }

    /// <summary>A circle around the mouse pointer on the show screen (S), so the audience can see where it is. Kept between shows.</summary>
    public bool Spotlight { get; private set; }

    public void SetSpotlight(bool on)
    {
        if (on == Spotlight) return;
        Spotlight = on;
        ShowStateChanged?.Invoke();
    }

    public void SetBlank(Blank b)
    {
        if (b == Blanked) return;
        Blanked = b;
        inkLog.Untrack(BlankInk); // a new blank screen starts empty: the old one's strokes can't come back
        BlankInk = new StrokeCollection();
        inkLog.Track(BlankInk);
        BlankInk.StrokesChanged += (_, _) => UpdateOverlay();
        ShowStateChanged?.Invoke();
        UpdateOverlay();
    }

    static string? Digit(Gesture g) => g.Mods != ModifierKeys.None ? null : g.Key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(g.Key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)(g.Key - Key.NumPad0)).ToString(),
        _ => null,
    };

    /// <summary>
    /// A key in the slide show. Digits type a slide number and Enter goes there; a lone digit with nothing after it
    /// for a second runs its own command instead (a pen color by default). Returns false when the key did nothing.
    /// </summary>
    public bool ShowKey(Gesture g)
    {
        if (Digit(g) is { } d)
        {
            jumpKey = Jump == "" ? g : null;
            SetJump(Jump.Length < 4 ? Jump + d : Jump);
            return true;
        }
        if (Jump != "")
        {
            var typed = Jump;
            switch (g)
            {
                case { Key: Key.Enter, Mods: ModifierKeys.None }:
                    SetJump("");
                    SetBlank(Blank.None);
                    if (int.TryParse(typed, out var n) && n >= 1) Go(n - 1);
                    return true;
                case { Key: Key.Back, Mods: ModifierKeys.None }:
                    jumpKey = null;
                    SetJump(typed[..^1]);
                    return true;
                case { Key: Key.Escape, Mods: ModifierKeys.None }:
                    SetJump("");
                    return true;
            }
            SetJump(""); // any other key drops the number and does its own thing
        }
        switch (Shortcuts.Find(g, inShow: true))
        {
            case null: return false;
            case "black": SetBlank(Blanked == Blank.Black ? Blank.None : Blank.Black); return true;
            case "white": SetBlank(Blanked == Blank.White ? Blank.None : Blank.White); return true;
            case "next" or "prev" or "first" or "last" when Blanked != Blank.None: SetBlank(Blank.None); return true; // like PowerPoint: back to the same slide
            case "laser": Pen.SetTool(Pen.Tool == Tool.Laser ? Tool.Select : Tool.Laser); return true;
            case "zoom": show?.ToggleZoom(); return true;
            case "spotlight": SetSpotlight(!Spotlight); return true;
            case "undo": UndoShowInk(); return true;
            case "clearInk": OverlayInk.Clear(); return true;
            case { } id: return Run(id);
        }
    }

    /// <summary>A key in the show or presenter window: <see cref="ShowKey"/>, then Esc = pointer, Esc again = end of the show.</summary>
    public bool ShowPress(Gesture g)
    {
        if (ShowKey(g)) return true;
        if (g is not { Key: Key.Escape, Mods: ModifierKeys.None }) return false;
        if (show?.Zooming == true) show.ResetZoom();
        else if (Pen.Tool != Tool.Select) Pen.SetTool(Tool.Select);
        else show?.Close();
        return true;
    }

    /// <summary>A click (or wheel) on the slide in pointer mode: brings a blanked slide back, otherwise moves on like PowerPoint.</summary>
    public void ShowAdvance(int dir)
    {
        if (show?.Zooming == true) show.ResetZoom(); // a click while zoomed goes back to the whole slide
        else if (Blanked != Blank.None) SetBlank(Blank.None);
        else Go(Current + dir);
    }

    void SetJump(string s)
    {
        Jump = s;
        jumpTimer.Stop();
        jumpTimer.Interval = TimeSpan.FromSeconds(jumpKey != null ? 1 : 4);
        if (s != "") jumpTimer.Start();
        ShowStateChanged?.Invoke();
    }

    void OnJumpTimeout(object? sender, EventArgs e)
    {
        var lone = jumpKey;
        jumpKey = null;
        SetJump("");
        if (lone is { } k && Shortcuts.Find(k, inShow: true) is { } id) Run(id);
    }
}
