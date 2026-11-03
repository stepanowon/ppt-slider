using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Win32;

namespace PptSlider;

/// <summary>
/// The window's side of editing: it hosts the editing parts (<see cref="IEditorHost"/>), wires the ribbon and the slide's
/// mouse events to them, keeps the font boxes in step, and does the slide-level commands (new, duplicate, delete, undo).
/// What an edit does lives in the parts: <see cref="StageInteraction"/>, <see cref="TextEditor"/>, <see cref="ShapeCommands"/>,
/// <see cref="TextCommands"/>, <see cref="TableCommands"/>, <see cref="InsertCommands"/>, <see cref="ClipboardCommands"/>,
/// <see cref="FormatPainterTool"/>.
/// </summary>
public partial class MainWindow : IEditorHost
{
    /// <summary>The editing parts this window hosts (built in the constructor, before anything can use them).</summary>
    readonly EditingParts editing;

    internal ShapeSelection Selection => editing.Selection;
    internal TextEditor TextEditor => editing.TextEditor;
    internal StageInteraction StageInput => editing.StageInput;
    internal ShapeCommands ShapeCommands => editing.Shapes;
    internal TextCommands TextCommands => editing.Text;
    internal TableCommands TableCommands => editing.Tables;
    internal InsertCommands InsertCommands => editing.Insert;
    internal ClipboardCommands ClipboardCommands => editing.Clipboard;
    internal FormatPainterTool FormatPainterTool => editing.Painter;

    // ---------- IEditorHost ----------

    IReadOnlyList<ShapeHit> IEditorHost.Hits => hits;
    FrameworkElement IEditorHost.Surface => Stage;
    Canvas IEditorHost.Marks => Overlay;
    Canvas? IEditorHost.Drawing => SlideHost.Content as Canvas;
    double IEditorHost.Px => Px;
    Brush IEditorHost.Accent => (Brush)FindResource("Accent");
    void IEditorHost.UseSelectTool() => Pen.SetTool(Tool.Select);
    void IEditorHost.Changed() => Changed();
    void IEditorHost.Status(string text) => StatusText.Text = text;
    void IEditorHost.SlideStatus() => UpdateStatus();
    void IEditorHost.SyncFormatUi() => SyncFormatUi();
    bool IEditorHost.IsFormatInput(DependencyObject? d) => InFormatBoxes(d);
    void IEditorHost.Warn(string message, string title) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>One screen pixel in slide units, so handles keep their size at any zoom.</summary>
    internal double Px
    {
        get
        {
            if (!Stage.IsVisible) return 1;
            var w = Stage.TransformToAncestor(this).TransformBounds(new Rect(Stage.RenderSize)).Width;
            return w > 0 ? Stage.Width / w : 1;
        }
    }

    void CommitEdit(bool cancel = false) => TextEditor.Commit(cancel);
    void DrawSelection() => editing.Adorner.Draw();

    /// <summary>Back to plain selecting: no shape to draw, no format to paint.</summary>
    void EndTools()
    {
        StageInput.CancelShape();
        FormatPainterTool.End();
    }

    // ---------- the slide's mouse ----------

    /// <summary>The self-test drives the slide itself: real mouse events (capture sends one from wherever the cursor is) are ignored.</summary>
    internal bool SyntheticInput;

    void OnStageDown(object sender, MouseButtonEventArgs e)
    {
        if (SyntheticInput || TextEditor.Box?.IsMouseOver == true || Pen.Tool != Tool.Select) return;
        if (StageInput.Down(e.GetPosition(Stage), Keyboard.Modifiers, e.ClickCount)) e.Handled = true;
    }

    void OnStageMove(object sender, MouseEventArgs e) { if (!SyntheticInput) StageInput.Move(e.GetPosition(Stage), Keyboard.Modifiers); }
    void OnStageUp(object sender, MouseButtonEventArgs e) { if (!SyntheticInput) StageInput.Up(); }

    // ---------- font boxes ----------

    bool InFormatBoxes(DependencyObject? d)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == FontBox || d == SizeBox || (d as FrameworkElement)?.TemplatedParent is { } tp && (tp == FontBox || tp == SizeBox)) return true;
        return false;
    }

    /// <summary>The font and size boxes show the editor's selected characters (blank when they differ), else the selected shape's.</summary>
    void SyncFormatUi()
    {
        syncing = true;
        if (TextEditor.Box is { } box)
        {
            SizeBox.Text = TextEditor.SizeOf(box.Selection)?.ToString() ?? "";
            FontBox.Text = TextEditor.SelectedFont;
        }
        else
        {
            var el = Selection.Primary;
            SizeBox.Text = TextFormat.HasFormat(el) ? TextCommands.SelectedSize.ToString() : "";
            FontBox.Text = el == null ? "" : TextFormat.FirstRunFont(el) ?? "";
        }
        syncing = false;
    }

    // The boxes keep the caret after they are used, and the thumbnails, the slide and the ribbon buttons can't take it, so
    // without this the slide keys (arrows, PgUp / PgDn) went on changing the box's choice, which applied a font to the slide.
    // A choice applies when it is picked from the open list or confirmed with Enter, and then the caret leaves the box;
    // a click anywhere else takes it out too.

    /// <summary>
    /// Takes the caret out of the font / size boxes unless the text editor has it: to the slide, whose keys then bubble to the
    /// window. (Focusing the window itself would hand the caret straight back: it remembers the box as its focused element.)
    /// </summary>
    void LeaveFormatBoxes()
    {
        if (TextEditor.IsEditing || !InFormatBoxes(Keyboard.FocusedElement as DependencyObject)) return;
        if (Stage.IsVisible && Stage.Focus()) return;
        FocusManager.SetFocusedElement(this, null); // no slide shown: nothing focused, keys still reach the window
        Keyboard.ClearFocus();
    }

    /// <summary>Window PreviewMouseDown: a click outside the boxes ends typing in them.</summary>
    void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!InFormatBoxes(e.OriginalSource as DependencyObject)) LeaveFormatBoxes();
    }

    /// <summary>After a choice was applied: once the list has closed, the caret goes back.</summary>
    void FormatChosen() => Dispatcher.BeginInvoke(LeaveFormatBoxes, System.Windows.Threading.DispatcherPriority.Input);

    void OnSizePicked(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || !SizeBox.IsDropDownOpen || SizeBox.SelectedItem is not double pt) return; // arrows with the list closed only browse
        TextCommands.SetFontSize(pt);
        FormatChosen();
    }

    void OnSizeKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SyncFormatUi(); LeaveFormatBoxes(); e.Handled = true; return; }
        if (e.Key != Key.Enter) return;
        if (double.TryParse(SizeBox.Text, out var pt) && pt is >= 1 and <= 400) TextCommands.SetFontSize(pt);
        FormatChosen();
        e.Handled = true;
    }

    void OnFontPicked(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || !FontBox.IsDropDownOpen || FontBox.SelectedItem is not string face) return;
        TextCommands.SetFontFace(face);
        FormatChosen();
    }

    void OnFontKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SyncFormatUi(); LeaveFormatBoxes(); e.Handled = true; return; }
        if (e.Key != Key.Enter) return;
        // a typed name applies when it is an installed font (in any case); the list's own spelling is used
        if (FontBox.Items.Cast<string>().FirstOrDefault(f => string.Equals(f, FontBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)) is { } face)
            TextCommands.SetFontFace(face);
        else SyncFormatUi();
        FormatChosen();
        e.Handled = true;
    }

    // ---------- ribbon: text ----------

    void OnGrow(object? s, RoutedEventArgs? e) => TextCommands.Grow();
    void OnShrink(object? s, RoutedEventArgs? e) => TextCommands.Shrink();
    void OnBold(object? s, RoutedEventArgs? e) => TextCommands.Toggle("bold");
    void OnItalic(object? s, RoutedEventArgs? e) => TextCommands.Toggle("italic");
    void OnUnderline(object? s, RoutedEventArgs? e) => TextCommands.Toggle("underline");
    void OnStrike(object? s, RoutedEventArgs? e) => TextCommands.Toggle("strike");
    void OnAlign(object s, RoutedEventArgs e) => TextCommands.SetAlign((string)((Button)s).CommandParameter);
    void OnBullets(object s, RoutedEventArgs e) => TextCommands.ToggleBullets("buChar");
    void OnNumbering(object s, RoutedEventArgs e) => TextCommands.ToggleBullets("buAutoNum");
    void OnIndentMore(object? s, RoutedEventArgs? e) => TextCommands.Indent(1);
    void OnIndentLess(object? s, RoutedEventArgs? e) => TextCommands.Indent(-1);
    void OnLineSpacing(object sender, RoutedEventArgs e) =>
        Menu(sender, [.. new[] { 1.0, 1.15, 1.5, 2.0, 2.5, 3.0 }.Select(m => (m.ToString("0.0#"), (Action)(() => TextCommands.SetLineSpacing(m))))]);
    void OnFontColor(object sender, RoutedEventArgs e) => ColorMenu(ColorBtn, TextColors, c => { if (c is { } v) TextCommands.SetColor(v); });
    void OnHighlight(object sender, RoutedEventArgs e) => ColorMenu((UIElement)sender, HighlightColors, TextCommands.SetHighlight, L.T("noHighlight"));

    static readonly Color[] HighlightColors =
    [
        Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0x00, 0xFF, 0x00), Color.FromRgb(0x00, 0xFF, 0xFF), Color.FromRgb(0xFF, 0x00, 0xFF),
        Color.FromRgb(0x00, 0x00, 0xFF), Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0x00, 0x00, 0x80), Color.FromRgb(0x00, 0x80, 0x80),
        Color.FromRgb(0x00, 0x80, 0x00), Color.FromRgb(0x80, 0x00, 0x80), Color.FromRgb(0x80, 0x00, 0x00), Color.FromRgb(0x80, 0x80, 0x00),
        Color.FromRgb(0x80, 0x80, 0x80), Color.FromRgb(0xC0, 0xC0, 0xC0), Colors.Black,
    ];

    // ---------- ribbon: insert / delete ----------

    void OnAddText(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        double w = Deck.Renderer.Width, h = Deck.Renderer.Height;
        InsertCommands.TextBox(L.T("newTextBox"), new Rect(w * 0.3, h * 0.42, w * 0.4, h * 0.12), edit: true);
    }

    void OnAddPicture(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        var dlg = new OpenFileDialog { Filter = L.T("pictureFilter") };
        if (dlg.ShowDialog(this) == true) InsertCommands.PictureFile(dlg.FileName);
    }

    void OnDeleteShape(object? sender, RoutedEventArgs? e) => ShapeCommands.Delete();

    // ---------- slides ----------

    void OnNewSlide(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        Deck.NewSlide(Current);
        InsertThumb(Current + 1);
        Go(Current + 1);
    }

    /// <summary>An empty slide after the current one to draw on, in the editor or the show; it is a real slide in the file.</summary>
    void OnBlankPage(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        CommitEdit();
        Deck.NewSlide(Current, blank: true);
        InsertThumb(Current + 1);
        Go(Current + 1);
        if (Pen.Tool is Tool.Select or Tool.Laser) Pen.SetTool(Tool.Pen);
    }

    void OnDuplicate(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        Deck.Duplicate(Current);
        InsertThumb(Current + 1);
        Go(Current + 1);
    }

    void OnDeleteSlide(object sender, RoutedEventArgs e)
    {
        if (Deck == null) return;
        if (Count <= 1) { StatusText.Text = L.T("lastSlideStays"); return; }
        if (!ConfirmDeleteSlide(Current + 1)) return;
        Ink.Forget(CurrentSlide!); // its strokes and their undo go with it
        int gone = Current;
        Deck.DeleteSlide(gone);
        RemoveThumb(gone);
        int i = Math.Min(Current, Count - 1);
        Current = -1;
        Go(i);
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e)
    {
        CommitEdit();
        // ink first: Ctrl+Z right after drawing takes back the stroke, not an earlier edit
        if (Ink.Undo(OverlayInk)) { StatusText.Text = L.T("inkUndone"); return; }
        if (Deck?.History.Undo() is not { } part) { StatusText.Text = L.T("nothingToUndo"); return; }
        Restored(part);
    }

    void OnRedo(object? sender, RoutedEventArgs? e)
    {
        CommitEdit();
        if (Deck?.History.Redo() is not { } part) { StatusText.Text = L.T("nothingToRedo"); return; }
        Restored(part);
    }

    /// <summary>After undo / redo replaced a slide: show it (its shapes are new objects, so nothing stays selected).</summary>
    void Restored(SlidePart part)
    {
        Selection.Reset();
        int i = Deck!.Slides.IndexOf(part);
        if (Thumbs.Items[i] is ListBoxItem item) item.Content = ThumbContent(i);
        Current = -1;
        Go(i);
        UpdateTitle();
    }
}
