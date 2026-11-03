using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace PptSlider;

/// <summary>
/// `PptSlider.exe --leak deck.pptx outDir`: repeats real operations and writes outDir/leak.txt with the memory after full GCs
/// and whether closed windows, editors and old decks are collected. Environment: LEAK_OPENS = how many times to reopen the
/// file (40); LEAK_COMPACT = 1 also compacts the large-object heap, 2 runs aggressive GCs that give free memory back to Windows
/// (tells retained-but-free GC memory from a leak).
/// </summary>
static partial class SelfTest
{
    public static int LeakProbe(string file, string outDir)
    {
        Directory.CreateDirectory(outDir);
        AppPaths.Local = Path.Combine(outDir, "local");
        InkOverlay.OffScreen = true;
        var log = new List<string>();
        void Log(string s) { log.Add(s); File.WriteAllLines(Path.Combine(outDir, "leak.txt"), log); }
        (long managed, long priv, long committed) Mem()
        {
            for (int i = 0; i < 3; i++) { Wait(30); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            switch (Environment.GetEnvironmentVariable("LEAK_COMPACT"))
            {
                case "1": // LOH compaction only
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                    break;
                case "2": // aggressive: gives free memory back, no LOH compaction setting
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                    break;
            }
            var p = Process.GetCurrentProcess(); p.Refresh();
            return (GC.GetTotalMemory(true) / 1024, p.PrivateMemorySize64 / 1024, GC.GetGCMemoryInfo().TotalCommittedBytes / 1024);
        }
        bool Alive(List<WeakReference> refs) { Mem(); return refs.Any(r => r.IsAlive); }
        int AliveCount(List<WeakReference> refs) { Mem(); return refs.Count(r => r.IsAlive); }

        void Series(string name, int rounds, Action step)
        {
            step(); // warm-up
            var m0 = Mem();
            var marks = new List<string>();
            for (int r = 1; r <= rounds; r++)
            {
                step();
                if (r == rounds / 4 || r == rounds / 2 || r == rounds)
                {
                    var m = Mem();
                    marks.Add($"{r}: managed {m.managed - m0.managed:+#;-#;0}KB private {m.priv - m0.priv:+#;-#;0}KB (GC heap committed {m.committed - m0.committed:+#;-#;0}KB)");
                }
            }
            Log($"[{name}] base managed {m0.managed}KB private {m0.priv}KB | " + string.Join(" | ", marks));
        }

        var main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, Width = 1400, Height = 860, ShowActivated = false };
        main.AskRecover = _ => false;
        try
        {
            main.Show();
            main.Open(file);
            Wait(200);
            int n = main.Count;

            // 1. navigation through every slide
            Series("navigate all slides", 20, () => { for (int i = 0; i < n; i++) main.Go(i); });

            // 2. renderer alone
            Series("render all slides (renderer only)", 20, () => { foreach (var s in main.Deck!.Slides) main.Deck.Renderer.Render(s); });

            // 3. slide show with presenter view and overlay
            main.PresenterBox.IsChecked = true;
            var wins = new List<WeakReference>();
            Series("slide show start/draw/close", 20, () =>
            {
                main.StartShow(0, test: true);
                Wait(50);
                var show = main.ShowView!;
                wins.Add(new WeakReference(show));
                if (main.PresenterView is { } pv) wins.Add(new WeakReference(pv));
                show.Press(new Gesture(Key.P, ModifierKeys.None));
                if (main.InkView is { } ov) wins.Add(new WeakReference(ov));
                show.Press(new Gesture(Key.Right, ModifierKeys.None));
                show.Press(new Gesture(Key.S, ModifierKeys.None)); // spotlight on (CompositionTarget.Rendering)
                show.Press(new Gesture(Key.S, ModifierKeys.None));
                show.Press(new Gesture(Key.B, ModifierKeys.None));
                show.Press(new Gesture(Key.B, ModifierKeys.None));
                show.Press(new Gesture(Key.Escape, ModifierKeys.None));
                main.ShowView?.Close();
                Wait(50);
            });
            main.Pen.SetTool(Tool.Select);
            Log($"  show windows still alive: {AliveCount(wins)} of {wins.Count} (the last show may be held for a moment)");
            foreach (var g in wins.Where(w => w.IsAlive).Select(w => w.Target!.GetType().Name).GroupBy(t => t)) Log($"    alive {g.Key} x{g.Count()}");

            // 4. reopening the file
            var decks = new List<WeakReference>();
            Series("open the same file again", int.TryParse(Environment.GetEnvironmentVariable("LEAK_OPENS"), out var no) ? no : 40, () => { decks.Add(new WeakReference(main.Deck)); main.Open(file); });
            Log($"  old decks still alive: {AliveCount(decks)} of {decks.Count}");

            // 5. text editing begin / cancel
            main.Go(0);
            var boxes = new List<WeakReference>();
            Series("text edit begin/cancel", 50, () =>
            {
                main.Go(0);
                var h = ((IEditorHost)main).Hits.FirstOrDefault(x => x.Text != null && TextContent.HasText(x.Element));
                if (h == null) return;
                main.Selection.Primary = h.Element;
                main.TextEditor.Begin();
                if (main.TextEditor.Box is { } b) boxes.Add(new WeakReference(b));
                Wait(5); // a quick edit: WPF formats the text in the background for a moment after it
                main.TextEditor.Commit(cancel: true);
            });

            Log($"  editor boxes still alive: {AliveCount(boxes)} of {boxes.Count}");
            Wait(3000);
            Log($"  editor boxes still alive 3s later: {AliveCount(boxes)} of {boxes.Count}");

            // 6. shape edits (each takes an undo snapshot of the slide's XML)
            int big = Enumerable.Range(0, n).MaxBy(i => main.Deck!.Slides[i].Slide!.OuterXml.Length);
            main.Go(big);
            var slideXml = main.CurrentSlide!.Slide!.OuterXml.Length;
            Series($"nudge a shape (slide XML {slideXml / 1024}KB) then undo half", 200, () =>
            {
                var h = ((IEditorHost)main).Hits.FirstOrDefault();
                if (h == null) return;
                main.Selection.Primary = h.Element;
                main.ShapeCommands.Nudge(8, 0);
                main.ShapeCommands.Nudge(-8, 0);
            });
            Log($"  undo snapshots kept: {main.Deck!.History.UndoCount} (limit {EditHistory.Limit})");

            // 7. ink draw and clear on every slide
            Series("ink: draw 5 strokes on each slide, then clear", 10, () =>
            {
                for (int i = 0; i < n; i++)
                {
                    main.Go(i);
                    var s = main.Ink.Of(main.CurrentSlide!);
                    for (int k = 0; k < 5; k++)
                        s.Add(new Stroke(new StylusPointCollection(Enumerable.Range(0, 50).Select(j => new StylusPoint(10 + j * 4, 10 + k * 20)))));
                    s.Clear();
                }
            });

            // 8. dialogs
            var dlg = new List<WeakReference>();
            Series("shortcut settings window open/close", 20, () =>
            {
                var w = new ShortcutsWindow { Owner = main, ShowActivated = false, Left = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
                dlg.Add(new WeakReference(w));
                w.Show(); Wait(20); w.Close();
            });
            Log($"  settings windows still alive: {AliveCount(dlg)} of {dlg.Count}");

            var brk = new List<WeakReference>();
            Series("break timer window open/close", 10, () =>
            {
                main.OpenBreak();
                var w = Application.Current.Windows.OfType<BreakWindow>().Last();
                brk.Add(new WeakReference(w));
                Wait(20); w.Close();
            });
            Log($"  break windows still alive: {AliveCount(brk)} of {brk.Count}");

            // 9. slide add/delete
            Series("new slide then delete it", 30, () =>
            {
                main.Go(0);
                main.Run("newSlide");
                main.ConfirmDeleteSlide = _ => true;
                typeof(MainWindow).GetMethod("OnDeleteSlide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(main, [null, null]);
                main.Go(0);
            });
        }
        catch (Exception ex) { Log("ERROR " + ex); }
        finally { main.CloseForTest(); }
        var mainRef = new WeakReference(main);
        main = null!;
        Log($"main window collected after close: {!Alive([mainRef])}");
        return 0;
    }
}
