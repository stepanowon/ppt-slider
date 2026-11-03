using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;

namespace PptSlider;

static partial class SelfTest
{
    /// <summary>Windows that are closed must not stay reachable from objects that live on (the slides' ink, static events).</summary>
    static void LeakTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow();
        var gone = new List<WeakReference>();
        try
        {
            main.Open(file);
            for (int i = 0; i < 3; i++)
            {
                main.StartShow(0, test: true);
                Wait(50);
                gone.Add(new WeakReference(main.ShowView));
                main.ShowView!.Press(new Gesture(Key.P, ModifierKeys.None)); // the overlay over the show
                gone.Add(new WeakReference(main.InkView));
                main.OverlayInk.Add(Line(10, 10, 200, 100));
                main.ShowView!.Close();
                Wait(50);
            }
            main.Pen.SetTool(Tool.Select);
            main.Ink.ClearAll();
            Wait(50);
            for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); Wait(20); }
            // the last show may still be held for a moment (input and focus bookkeeping); the ones before it must be gone
            check(gone.SkipLast(2).All(w => !w.IsAlive), $"leak: closed show windows and their ink overlays are collected (alive: {string.Join(",", gone.Select((w, i) => w.IsAlive ? i.ToString() : null).Where(s => s != null))} of {gone.Count})");
        }
        finally { main.CloseForTest(); }
        HistoryLimitTests(file, check);
        var handlers = typeof(Shortcuts).GetField("Changed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as Delegate;
        check(handlers?.GetInvocationList().All(d => d.Target != main) ?? true, "leak: a closed main window leaves the static shortcut event");
    }

    /// <summary>Undo keeps only the newest edits and ink changes, so a long session doesn't keep growing.</summary>
    static void HistoryLimitTests(string file, Action<bool, string> check)
    {
        using (var deck = Deck.Open(file))
        {
            var sp = deck.Slides[0];
            var shape = sp.Slide!.Descendants<DocumentFormat.OpenXml.Presentation.Shape>().First(s => s.TextBody != null);
            var original = TextContent.GetText(shape);
            for (int i = 0; i < EditHistory.Limit + 20; i++)
            {
                deck.History.Snapshot(sp);
                TextContent.SetText(shape, $"edit {i}");
            }
            check(deck.History.UndoCount == EditHistory.Limit, $"history: slide edits keep the last {EditHistory.Limit} ({deck.History.UndoCount})");
            int undone = 0;
            while (deck.History.Undo() != null) undone++;
            shape = deck.Slides[0].Slide!.Descendants<DocumentFormat.OpenXml.Presentation.Shape>().First(s => s.TextBody != null);
            check(undone == EditHistory.Limit && TextContent.GetText(shape) == "edit 19" && TextContent.GetText(shape) != original,
                $"history: undo goes back {EditHistory.Limit} edits, the oldest are gone ({undone}, \"{TextContent.GetText(shape)}\")");
        }
        var log = new InkHistory();
        var strokes = new StrokeCollection();
        log.Track(strokes);
        for (int i = 0; i < InkHistory.Limit + 30; i++) strokes.Add(Line(i, 0, i, 10));
        int back = 0;
        while (log.Undo(strokes)) back++;
        check(back == InkHistory.Limit && strokes.Count == 30, $"history: ink undo keeps the last {InkHistory.Limit} changes ({back}, {strokes.Count} strokes left)");
    }
}
