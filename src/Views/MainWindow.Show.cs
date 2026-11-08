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
        Session.Begin();
        var shown = SelectedMonitor;
        show = new ShowWindow(this, shown, test);
        show.Closed += (_, _) =>
        {
            show = null;
            presenter?.Close();
            Session.End();
            if (Pen.Tool != Tool.Select) Pen.SetTool(Tool.Select); // no laser or ink over the editor
            SyncDrawTools();
            UpdateOverlay(); // the ink goes away with the show (it stays with its slides for the next show and the PDF)
            Activate();
        };
        show.Show();
        SyncDrawTools();
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


    // ---------- slide show keys (shared by the show window, the presenter view and the overlay over the show) ----------

    /// <summary>
    /// A key in the slide show. Digits type a slide number and Enter goes there; a lone digit with nothing after it
    /// for a second runs its own command instead (a pen color by default). Returns false when the key did nothing.
    /// </summary>
    public bool ShowKey(Gesture g)
    {
        if (Session.TypeKey(g, out var goTo))
        {
            if (goTo is { } n) Go(n - 1);
            return true;
        }
        switch (Shortcuts.Find(g, inShow: true))
        {
            case null: return false;
            case "black": Session.ToggleBlank(Blank.Black); return true;
            case "white": Session.ToggleBlank(Blank.White); return true;
            case "next" or "prev" or "first" or "last" when Session.Blanked != Blank.None: Session.SetBlank(Blank.None); return true; // like PowerPoint: back to the same slide
            case "laser": Pen.SetTool(Pen.Tool == Tool.Laser ? Tool.Select : Tool.Laser); return true;
            case "zoom": show?.ToggleZoom(); return true;
            case "spotlight": Session.SetSpotlight(!Session.Spotlight); return true;
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
        else if (Session.Blanked != Blank.None) Session.SetBlank(Blank.None);
        else Go(Current + dir);
    }
}
