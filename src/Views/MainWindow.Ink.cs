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

/// <summary>Screen ink drawn on the InkOverlay: per-slide strokes, tools, undo, ink for PDF export.</summary>
public partial class MainWindow
{
    // ---------- screen ink (InkOverlay) ----------

    /// <summary>The strokes on the screen now: the blank screen's in the show while blanked, else the current slide's (or, with no file, the desktop's).</summary>
    public StrokeCollection OverlayInk => show != null && Blanked != Blank.None ? BlankInk : CurrentSlide is { } s ? StrokesOf(s) : desktopInk;

    /// <summary>A drawing tool is picked: the overlay takes the mouse.</summary>
    public bool Drawing => Pen.Tool is Tool.Pen or Tool.Highlighter or Tool.Eraser;

    internal InkOverlay? InkView => overlay;
    internal bool InkHidden => inkHidden;

    /// <summary>
    /// Shows the overlay while drawing or while the current slide has ink (unless hidden), over the show's monitor during a show,
    /// otherwise over the editor's.
    /// </summary>
    internal void UpdateOverlay()
    {
        if (closing) return;
        if (Drawing) inkHidden = false;
        var strokes = OverlayInk;
        InkShownBtn.IsChecked = !inkHidden;
        if (overlay != null) overlay.Strokes = strokes;
        if (inkHidden || !(Drawing || strokes.Count > 0)) { overlay?.Hide(); SyncShowBar(); return; }
        var target = show?.Monitor ?? Monitors.Of(this);
        if (overlay != null && (overlay.Monitor != target || overlay.Over != show)) { overlay.Close(); overlay = null; }
        overlay ??= new InkOverlay(this, target, show);
        overlay.Strokes = strokes;
        bool wasDrawing = overlay.IsVisible && overlay.Ink.IsHitTestVisible;
        if (!overlay.IsVisible) overlay.Show();
        if (Drawing && !wasDrawing && !InkOverlay.OffScreen) overlay.Activate(); // its keys: next slide, Esc...
        SyncShowBar();
    }

    /// <summary>
    /// One toolbar in the show: while the overlay is over it, the overlay's (the show's own would be underneath it and,
    /// while drawing, out of the mouse's reach); otherwise the show's.
    /// </summary>
    void SyncShowBar()
    {
        if (show == null) return;
        show.Bar.Visibility = overlay is { IsVisible: true } && overlay.Over == show ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The overlay's ✕: hides the ink and goes back to the pointer; picking a drawing tool shows it again.</summary>
    internal void HideInk()
    {
        inkHidden = true;
        Pen.SetTool(Tool.Select);
        UpdateOverlay();
        if (show == null) Activate();
    }

    void OnInkShown(object sender, RoutedEventArgs e)
    {
        inkHidden = InkShownBtn.IsChecked != true;
        if (inkHidden && Drawing) Pen.SetTool(Tool.Select);
        UpdateOverlay();
    }

    /// <summary>A key on the overlay: the show's keys during a show, otherwise the editor's commands; Esc = pointer.</summary>
    internal bool InkPress(Gesture g)
    {
        if (show != null) return ShowPress(g);
        if (g is { Key: Key.Escape, Mods: ModifierKeys.None }) { Pen.SetTool(Tool.Select); return true; }
        return Shortcuts.Find(g, inShow: false) is { } id && Run(id);
    }

    static readonly Guid FrameId = new("6E3B1C2A-4F4D-4E8B-9C1A-2D7B5E0F9A31");

    /// <summary>
    /// Remembers where the slide was on the overlay when <paramref name="s"/> was drawn (the show's slide, or the editor's),
    /// so <see cref="SlideInk"/> can put the stroke on the slide for a PDF.
    /// </summary>
    internal void TagStroke(Stroke s, InkOverlay on)
    {
        FrameworkElement? stage = show != null ? (Blanked == Blank.None ? show.Stage : null) : IsVisible && StageFrame.IsVisible && WindowState != WindowState.Minimized ? Stage : null;
        if (stage is not { IsVisible: true } || PresentationSource.FromVisual(on) == null) return;
        var a = on.PointFromScreen(stage.PointToScreen(new Point(0, 0)));
        var b = on.PointFromScreen(stage.PointToScreen(new Point(stage.Width, stage.Height)));
        if (b.X - a.X < 1 || b.Y - a.Y < 1) return;
        s.AddPropertyData(FrameId, new[] { a.X, a.Y, b.X - a.X, b.Y - a.Y });
    }

    /// <summary>Each slide's strokes that were drawn over the slide, moved onto it (slide coordinates); slides without any are left out.</summary>
    internal Dictionary<SlidePart, StrokeCollection> SlideInk()
    {
        var result = new Dictionary<SlidePart, StrokeCollection>();
        if (Deck == null) return result;
        double w = Deck.Renderer.Width, h = Deck.Renderer.Height;
        foreach (var sp in Deck.Slides)
        {
            if (!ink.TryGetValue(sp, out var strokes)) continue;
            var on = new StrokeCollection();
            foreach (var st in strokes)
            {
                if (!st.ContainsPropertyData(FrameId) || st.GetPropertyData(FrameId) is not double[] { Length: 4 } f) continue;
                double sx = w / f[2], sy = h / f[3];
                var c = st.Clone();
                var m = Matrix.Identity;
                m.Translate(-f[0], -f[1]);
                m.Scale(sx, sy);
                c.Transform(m, false);
                c.DrawingAttributes.Width *= sx;
                c.DrawingAttributes.Height *= sy;
                on.Add(c);
            }
            if (on.Count > 0) result[sp] = on;
        }
        return result;
    }

    void OnPenChanged()
    {
        if (Pen.Tool != Tool.Select) { CommitEdit(); selected = null; pendingShape = null; EndPainter(); Stage.Cursor = null; DrawSelection(); }
        UpdateOverlay();
        ToolSelect.IsChecked = Pen.Tool == Tool.Select;
        ToolPen.IsChecked = Pen.Tool == Tool.Pen;
        ToolHi.IsChecked = Pen.Tool == Tool.Highlighter;
        ToolEraser.IsChecked = Pen.Tool == Tool.Eraser;
        syncing = true;
        SizeSlider.Minimum = Pen.IsHighlighter ? 6 : 1;
        SizeSlider.Maximum = Pen.IsHighlighter ? 80 : 40;
        SizeSlider.Value = Pen.Size;
        syncing = false;
        SizeText.Text = Pen.Size.ToString("0");
        PenPreview.Width = PenPreview.Height = Math.Min(Pen.Size, 30);
        PenPreview.Fill = new SolidColorBrush(Pen.Color);
        PenPreview.Stroke = Brushes.Gray;
        ToolText.Text = Pen.Tool switch
        {
            Tool.Pen => L.F("penStatus", Pen.PenSize),
            Tool.Highlighter => L.F("highlighterStatus", Pen.HiSize),
            Tool.Eraser => L.T("eraser"),
            Tool.Laser => L.T("laser"),
            _ => L.T("select"),
        };
    }

    void OnTool(object sender, RoutedEventArgs e) =>
        Pen.SetTool(sender == ToolPen ? Tool.Pen : sender == ToolHi ? Tool.Highlighter : sender == ToolEraser ? Tool.Eraser : Tool.Select);

    void OnPenSize(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!syncing) Pen.SetSize(Math.Round(e.NewValue));
    }

    public void ClearInk() => OverlayInk.Clear();

    void OnClearInk(object sender, RoutedEventArgs e) => ClearInk();

    /// <summary>Takes back the last pen stroke (or erase) on the current slide; slide edits are left alone.</summary>
    public void UndoInk()
    {
        CommitEdit();
        StatusText.Text = inkLog.Undo(OverlayInk) ? L.T("inkUndone") : L.T("noInkToUndo");
    }

    void OnUndoInk(object sender, RoutedEventArgs e) => UndoInk();

    /// <summary>The show's and presenter's undo button: the last stroke on the slide being shown.</summary>
    internal void UndoShowInk() => inkLog.Undo(OverlayInk);

    void OnClearAllInk(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, L.T("clearAllAsk"), L.T("clearInkTitle"), MessageBoxButton.OKCancel) == MessageBoxResult.OK) ClearAllInk();
    }

    internal void ClearAllInk()
    {
        foreach (var s in ink.Values) s.Clear();
        desktopInk.Clear();
    }
}
