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

/// <summary>Selection, move / resize, in-place text editing, formatting, inserting and deleting shapes and slides, undo.</summary>
public partial class MainWindow
{
    // ---------- selection, move, resize, in-place text edit ----------

    ShapeHit? SelectedHit => hits.FirstOrDefault(h => h.Element == selected);
    internal OpenXmlElement? SelectedElement => selected;

    /// <summary>One screen pixel in slide units, so handles keep their size at any zoom.</summary>
    double Px
    {
        get
        {
            if (!Stage.IsVisible) return 1;
            var w = Stage.TransformToAncestor(this).TransformBounds(new Rect(Stage.RenderSize)).Width;
            return w > 0 ? Stage.Width / w : 1;
        }
    }

    void DrawSelection(Rect? preview = null)
    {
        foreach (var c in Overlay.Children.OfType<Shape>().ToList()) Overlay.Children.Remove(c);
        if (SelectedHit is not { } h) return;
        var r = preview ?? h.Bounds;
        double px = Px;
        var box = new Rectangle { Width = r.Width, Height = r.Height, Stroke = (Brush)FindResource("Accent"), StrokeThickness = 1.5 * px, StrokeDashArray = [4, 2], IsHitTestVisible = false };
        var handle = new Rectangle { Width = 9 * px, Height = 9 * px, Fill = Brushes.White, Stroke = (Brush)FindResource("Accent"), StrokeThickness = 1.5 * px, IsHitTestVisible = false };
        Canvas.SetLeft(box, r.X); Canvas.SetTop(box, r.Y);
        Canvas.SetLeft(handle, r.Right - 4.5 * px); Canvas.SetTop(handle, r.Bottom - 4.5 * px);
        Overlay.Children.Add(box);
        Overlay.Children.Add(handle);
    }

    void OnStageDown(object sender, MouseButtonEventArgs e)
    {
        if (Deck == null || Pen.Tool != Tool.Select || editBox?.IsMouseOver == true) return;
        CommitEdit();
        var p = e.GetPosition(Stage);
        double px = Px;
        if (SelectedHit is { } cur && (p - cur.Bounds.BottomRight).Length < 10 * px)
            resizing = true;
        else
        {
            resizing = false;
            selected = hits.LastOrDefault(h => h.Bounds.Contains(p))?.Element; // last drawn = topmost
            SyncFormatUi();
            if (selected == null) { DrawSelection(); return; }
            if (e.ClickCount == 2 && Deck.HasText(selected)) { BeginEdit(p); return; }
        }
        dragging = true;
        dragStart = p;
        dragOrig = dragRect = SelectedHit!.Bounds;
        Stage.CaptureMouse();
        DrawSelection();
        e.Handled = true;
    }

    void OnStageMove(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        var d = e.GetPosition(Stage) - dragStart;
        dragRect = resizing
            ? new Rect(dragOrig.X, dragOrig.Y, Math.Max(8, dragOrig.Width + d.X), Math.Max(8, dragOrig.Height + d.Y))
            : new Rect(dragOrig.X + d.X, dragOrig.Y + d.Y, dragOrig.Width, dragOrig.Height);
        DrawSelection(dragRect);
    }

    void OnStageUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        Stage.ReleaseMouseCapture();
        if (selected == null || (Math.Abs(dragRect.X - dragOrig.X) < 0.5 && Math.Abs(dragRect.Y - dragOrig.Y) < 0.5
                                 && Math.Abs(dragRect.Width - dragOrig.Width) < 0.5 && Math.Abs(dragRect.Height - dragOrig.Height) < 0.5)) return;
        Deck!.Snapshot(CurrentSlide!);
        Deck.SetBounds(selected, dragRect);
        Changed();
    }

    /// <summary>
    /// Edits the selected shape's text in place: the editor shows the text exactly as drawn (fonts, sizes, colors,
    /// bold, alignment...) on top of the hidden original. <paramref name="caretAt"/> (slide coordinates) places the caret,
    /// otherwise all text is selected.
    /// </summary>
    void BeginEdit(Point? caretAt = null)
    {
        if (SelectedHit is not { } h || !Deck.HasText(h.Element)) return;
        double bt = 2 * Px;
        FlowDocument doc;
        Thickness pad = default;
        Point origin = h.Bounds.TopLeft;
        if (h.Text is { } panel && panel.IsVisible)
        {
            doc = SlideRenderer.EditDocument(panel);
            editScale = panel.Tag as double? ?? Deck.FontScale(h.Element); // shrunk to fit: the editor shows it that size
            pad = panel.Margin;
            origin = panel.TranslatePoint(new Point(-pad.Left, -pad.Top), Stage);
            origin.X = h.Bounds.X; // the panel stretches to the shape's width
            editHidden = panel;
            panel.Visibility = Visibility.Hidden;
        }
        else
        {
            doc = new FlowDocument { PagePadding = new Thickness(0) };
            editScale = 1;
            foreach (var line in Deck.GetEditText(h.Element).Split('\n'))
                doc.Blocks.Add(new Paragraph(new Run(line.Replace('\v', '\n'))) { Margin = default });
            doc.FontSize = (double.TryParse(Deck.FirstRun(h.Element, "sz"), out var sz) ? sz / 100 : 18) * 96 / 72;
        }
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph { Margin = default });
        doc.FontFamily = new FontFamily("Malgun Gothic"); // paragraphs set their own; this is for an empty box

        var box = new RichTextBox
        {
            Document = doc, Tag = h.Element,
            Width = h.Bounds.Width + 2 * bt, MinHeight = h.Bounds.Height + 2 * bt,
            Padding = pad, Background = Brushes.Transparent, AllowDrop = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            BorderBrush = (Brush)FindResource("Accent"), BorderThickness = new Thickness(bt),
        };
        // plain-text paste; bold / italic / underline go through the user's shortcuts (OnKey), other formatting is not kept
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText)) e.FormatToApply = DataFormats.UnicodeText;
            else e.CancelCommand();
        });
        foreach (var cmd in new[] { EditingCommands.ToggleBold, EditingCommands.ToggleItalic, EditingCommands.ToggleUnderline,
                     EditingCommands.IncreaseFontSize, EditingCommands.DecreaseFontSize, EditingCommands.AlignLeft, EditingCommands.AlignCenter,
                     EditingCommands.AlignRight, EditingCommands.AlignJustify, EditingCommands.ToggleBullets, EditingCommands.ToggleNumbering,
                     EditingCommands.IncreaseIndentation, EditingCommands.DecreaseIndentation, EditingCommands.ToggleSubscript, EditingCommands.ToggleSuperscript })
            box.CommandBindings.Add(new CommandBinding(cmd, (_, e) => e.Handled = true, (_, e) => { e.CanExecute = false; e.Handled = true; }));
        Canvas.SetLeft(box, origin.X - bt); Canvas.SetTop(box, origin.Y - bt);
        editBox = box;
        editStart = EditText(doc).Fmt;
        // picking a font or size for the selection moves focus to the boxes: the edit goes on
        box.LostKeyboardFocus += (_, e) => { if (!InFormatBoxes(e.NewFocus as DependencyObject)) CommitEdit(); };
        box.SelectionChanged += (_, _) => { if (editBox == box) SyncFormatUi(); };
        Overlay.Children.Add(box);
        box.Focus();
        SyncFormatUi();
        if (caretAt is { } pt)
            Dispatcher.BeginInvoke(() =>
            {
                if (editBox == box && box.GetPositionFromPoint(Stage.TranslatePoint(pt, box), true) is { } pos) box.CaretPosition = pos;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        else box.SelectAll();
        StatusText.Text = L.T("editingText");
    }

    /// <summary>The editor's text in <see cref="Deck.GetEditText"/> form, with each character's bold / italic / underline / font / size.</summary>
    internal static (string Text, List<Deck.Fmt> Fmt) EditText(FlowDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        var fmt = new List<Deck.Fmt>();
        bool first = true;
        void Add(char c, Deck.Fmt f = default) { sb.Append(c); fmt.Add(f); }
        void Inlines(InlineCollection inl)
        {
            foreach (var i in inl)
                switch (i)
                {
                    case Run r:
                        // underline can sit on the run or any span / paragraph around it; weight and style are inherited
                        bool u = false;
                        for (DependencyObject? e = r; e is TextElement te && !u; e = te.Parent)
                            u = (e as Inline)?.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true
                                || (e as Paragraph)?.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true;
                        var f = new Deck.Fmt(r.FontWeight >= FontWeights.Bold, r.FontStyle == FontStyles.Italic, u, r.FontFamily?.Source, r.FontSize * 72 / 96);
                        foreach (var c in r.Text.Replace("\r\n", "\n").Replace('\r', '\n')) Add(c == '\n' ? '\v' : c, f);
                        break;
                    case LineBreak: Add('\v'); break;
                    case Span sp: Inlines(sp.Inlines); break;
                }
        }
        void Blocks(BlockCollection blocks)
        {
            foreach (var b in blocks)
                switch (b)
                {
                    case Paragraph p: if (!first) Add('\n'); first = false; Inlines(p.Inlines); break;
                    case Section s: Blocks(s.Blocks); break;
                    case List l: foreach (var item in l.ListItems) Blocks(item.Blocks); break;
                }
        }
        Blocks(doc.Blocks);
        return (sb.ToString(), fmt);
    }

    /// <summary>Bold / italic / underline on the editor's selection (or for the text typed next), like PowerPoint.</summary>
    void ToggleInEditor(string id) => Toggle(editBox!.Selection, id);

    internal static void Toggle(TextRange sel, string id)
    {
        switch (id)
        {
            case "bold":
                bool b = sel.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w >= FontWeights.Bold;
                sel.ApplyPropertyValue(TextElement.FontWeightProperty, b ? FontWeights.Normal : FontWeights.Bold);
                break;
            case "italic":
                bool i = sel.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic;
                sel.ApplyPropertyValue(TextElement.FontStyleProperty, i ? FontStyles.Normal : FontStyles.Italic);
                break;
            case "underline":
                bool u = sel.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection d && d.Any(x => x.Location == TextDecorationLocation.Underline);
                sel.ApplyPropertyValue(Inline.TextDecorationsProperty, u ? new TextDecorationCollection() : TextDecorations.Underline);
                break;
        }
    }

    void CommitEdit(bool cancel = false)
    {
        if (editBox is not { } box) return;
        editBox = null; // first: removing the box fires LostKeyboardFocus again
        Overlay.Children.Remove(box);
        if (editHidden != null) editHidden.Visibility = Visibility.Visible;
        editHidden = null;
        UpdateStatus();
        SyncFormatUi();
        var el = (OpenXmlElement)box.Tag;
        var (text, fmt) = EditText(box.Document);
        if (cancel || (text == Deck.GetEditText(el) && fmt.SequenceEqual(editStart))) return;
        Deck!.Snapshot(CurrentSlide!);
        Deck.SetEditText(el, text, fmt, editStart, editScale);
        Changed();
    }

    // ---------- formatting ----------

    /// <summary>Runs an edit on the selected text shape with undo + redraw.</summary>
    void EditSelected(Action<OpenXmlElement> f)
    {
        CommitEdit();
        if (Deck == null || selected == null || !Deck.HasText(selected))
        {
            StatusText.Text = L.T("selectTextFirst");
            return;
        }
        Deck.Snapshot(CurrentSlide!);
        f(selected);
        Changed();
    }

    /// <summary>While editing text: on the editor's selection (or for the text typed next), otherwise on the whole shape.</summary>
    void SetFontSize(double pt)
    {
        if (editBox is { } box)
        {
            box.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, pt * editScale * 96 / 72);
            BackToEditor();
        }
        else EditSelected(s => Deck.EditRuns(s, r => Deck.SetAttr(r, "sz", ((int)Math.Round(pt * 100)).ToString())));
    }

    void SetFontFace(string face)
    {
        if (editBox is { } box)
        {
            box.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(face));
            BackToEditor();
        }
        else EditSelected(s => Deck.EditRuns(s, r => Deck.SetFont(r, face)));
    }

    /// <summary>Focus back to the text editor after the font / size boxes, once the drop-down has closed.</summary>
    void BackToEditor()
    {
        var box = editBox;
        Dispatcher.BeginInvoke(() => { if (editBox == box) box?.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
        SyncFormatUi();
    }

    bool InFormatBoxes(DependencyObject? d)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == FontBox || d == SizeBox || (d as FrameworkElement)?.TemplatedParent is { } tp && (tp == FontBox || tp == SizeBox)) return true;
        return false;
    }

    /// <summary>Points of the editor's selection as stored in the file (null: mixed sizes).</summary>
    double? EditorSize(TextRange sel) =>
        sel.GetPropertyValue(TextElement.FontSizeProperty) is double px ? Math.Round(px * 72 / 96 / editScale, 1) : null;

    double SelectedSize => editBox is { } box
        ? EditorSize(box.Selection) ?? EditorSize(new TextRange(box.Selection.Start, box.Selection.Start)) ?? 18 // mixed: the size where it starts
        : selected != null && double.TryParse(Deck.FirstRun(selected, "sz"), out var v) ? v / 100 : 18;

    void ToggleRun(string attr, string on, string off)
    {
        if (selected == null) { EditSelected(_ => { }); return; }
        var value = Deck.FirstRun(selected, attr) is { } v && v != off && v != "0" ? off : on;
        EditSelected(s => Deck.EditRuns(s, r => Deck.SetAttr(r, attr, value)));
    }

    void SyncFormatUi()
    {
        syncing = true;
        if (editBox is { } box) // the selected characters; blank when they differ
        {
            SizeBox.Text = EditorSize(box.Selection)?.ToString() ?? "";
            FontBox.Text = box.Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily f ? f.Source.Split(',')[0].Trim() : "";
        }
        else
        {
            SizeBox.Text = selected != null && Deck.HasText(selected) ? SelectedSize.ToString() : "";
            FontBox.Text = selected == null ? "" : Deck.FirstRunFont(selected) ?? "";
        }
        syncing = false;
    }

    void OnSizePicked(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && SizeBox.SelectedItem is double pt) SetFontSize(pt);
    }

    void OnSizeKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && double.TryParse(SizeBox.Text, out var pt) && pt is >= 1 and <= 400) SetFontSize(pt);
    }

    void OnFontPicked(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && FontBox.SelectedItem is string face) SetFontFace(face);
    }

    void OnGrow(object? s, RoutedEventArgs? e) => SetFontSize(FontSizes.FirstOrDefault(v => v > SelectedSize, SelectedSize + 8));
    void OnShrink(object? s, RoutedEventArgs? e) => SetFontSize(FontSizes.LastOrDefault(v => v < SelectedSize, Math.Max(1, SelectedSize - 1)));
    // while editing text these apply to the selection, otherwise to the whole shape
    void OnBold(object? s, RoutedEventArgs? e) { if (editBox != null) ToggleInEditor("bold"); else ToggleRun("b", "1", "0"); }
    void OnItalic(object? s, RoutedEventArgs? e) { if (editBox != null) ToggleInEditor("italic"); else ToggleRun("i", "1", "0"); }
    void OnUnderline(object? s, RoutedEventArgs? e) { if (editBox != null) ToggleInEditor("underline"); else ToggleRun("u", "sng", "none"); }
    void OnAlign(object s, RoutedEventArgs e) => EditSelected(sh => Deck.SetAlign(sh, (string)((Button)s).CommandParameter));

    void OnFontColor(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ColorBtn, Placement = PlacementMode.Bottom };
        var panel = new WrapPanel { Width = 160 };
        foreach (var c in TextColors)
        {
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(2), Background = new SolidColorBrush(c), BorderBrush = Brushes.Gray, Focusable = false };
            b.Click += (_, _) => { menu.IsOpen = false; EditSelected(sh => Deck.EditRuns(sh, r => Deck.SetColor(r, c))); };
            panel.Children.Add(b);
        }
        menu.Items.Add(panel);
        menu.IsOpen = true;
    }

    // ---------- insert / delete ----------

    void OnAddText(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        CommitEdit();
        Pen.SetTool(Tool.Select);
        double w = Deck.Renderer.Width, h = Deck.Renderer.Height;
        Deck.Snapshot(CurrentSlide!);
        selected = Deck.AddTextBox(CurrentSlide!, new Rect(w * 0.3, h * 0.42, w * 0.4, h * 0.12), L.T("newTextBox"));
        Changed();
        BeginEdit();
    }

    void OnAddPicture(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        var dlg = new OpenFileDialog { Filter = L.T("pictureFilter") };
        if (dlg.ShowDialog(this) == true) InsertPictureFile(dlg.FileName);
    }

    internal bool InsertPictureFile(string path)
    {
        if (Deck.PictureType(System.IO.Path.GetExtension(path)) is not { } type) return false;
        try { return InsertPicture(File.ReadAllBytes(path), type); }
        catch (Exception ex) { MessageBox.Show(this, L.F("pictureFail", ex.Message), L.T("picture"), MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
    }

    /// <summary>Ctrl+V: a picture on the clipboard (copied image, or an image file copied in Explorer) goes on the slide.</summary>
    internal bool PastePicture()
    {
        if (Deck == null) return false;
        if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(f => Deck.PictureType(System.IO.Path.GetExtension(f)) != null) is { } f)
            return InsertPictureFile(f);
        if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } img) return false;
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return InsertPicture(ms.ToArray(), Deck.PictureType(".png")!.Value);
    }

    /// <summary>Centered on the current slide at its own size, shrunk to fit 60% of the slide; selected afterwards.</summary>
    internal bool InsertPicture(byte[] image, DocumentFormat.OpenXml.Packaging.PartTypeInfo type)
    {
        if (Deck == null || CurrentSlide == null) return false;
        System.Windows.Media.Imaging.BitmapFrame frame;
        try { frame = System.Windows.Media.Imaging.BitmapDecoder.Create(new MemoryStream(image), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0]; }
        catch (Exception ex) { MessageBox.Show(this, L.F("pictureFail", ex.Message), L.T("picture"), MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        CommitEdit();
        Pen.SetTool(Tool.Select);
        double sw = Deck.Renderer.Width, sh = Deck.Renderer.Height;
        double w = frame.PixelWidth, h = frame.PixelHeight;
        double s = Math.Min(1, Math.Min(sw * 0.6 / w, sh * 0.6 / h));
        w *= s; h *= s;
        Deck.Snapshot(CurrentSlide);
        selected = Deck.AddPicture(CurrentSlide, image, type, new Rect((sw - w) / 2, (sh - h) / 2, w, h));
        Changed();
        return true;
    }

    void OnDeleteShape(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null || selected == null) return;
        Deck.Snapshot(CurrentSlide!);
        selected.Remove();
        selected = null;
        Changed();
    }

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
        if (ink.Remove(CurrentSlide!, out var strokes)) inkLog.Untrack(strokes); // its undo history goes with it
        int gone = Current;
        Deck.DeleteSlide(gone);
        RemoveThumb(gone);
        int i = Math.Min(Current, Count - 1);
        Current = -1;
        Go(i);
    }

    void OnUndo(object? sender, RoutedEventArgs? e)
    {
        CommitEdit();
        // ink first: Ctrl+Z right after drawing takes back the stroke, not an earlier edit
        if (inkLog.Undo(OverlayInk)) { StatusText.Text = L.T("inkUndone"); return; }
        if (Deck?.Undo() is not { } part) { StatusText.Text = L.T("nothingToUndo"); return; }
        selected = null;
        int i = Deck.Slides.IndexOf(part);
        if (Thumbs.Items[i] is ListBoxItem item) item.Content = ThumbContent(i);
        Current = -1;
        Go(i);
        UpdateTitle();
    }
}
