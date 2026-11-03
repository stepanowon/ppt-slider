using System.Windows;

namespace PptSlider;

/// <summary>
/// Handlers a window hooks onto objects that outlive it (the main window, the pen, the show), each registered with its
/// unhooking, and all unhooked when the window closes. A handler left on a longer-lived object keeps the closed window
/// (and everything it shows) in memory.
/// </summary>
sealed class WindowHooks
{
    readonly List<Action> undo = [];

    public WindowHooks(Window w) => w.Closed += (_, _) => UnhookAll();

    /// <summary>Runs <paramref name="hook"/> now and <paramref name="unhook"/> when the window closes.</summary>
    public void Add(Action hook, Action unhook)
    {
        hook();
        undo.Add(unhook);
    }

    /// <summary>Runs <paramref name="release"/> when the window closes (to let go of what it shows, stop its timers...).</summary>
    public void OnClose(Action release) => undo.Add(release);

    void UnhookAll()
    {
        foreach (var u in undo) u();
        undo.Clear();
    }
}
