using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>
/// Editing a shape's or a table cell's text in place: a rich text box over the hidden drawn text that shows it exactly
/// as drawn; when it closes, the edit goes back into the slide with each character's formatting (<see cref="TextContent.SetEditText"/>).
/// </summary>
sealed class TextEditor(IEditorHost host, ShapeSelection selection, SelectionAdorner adorner)
{
    FrameworkElement? hidden; // the drawn text the editor covers
    List<TextContent.Fmt> start = []; // the formatting per character when editing began

    /// <summary>The editor while text is being edited.</summary>
    public RichTextBox? Box { get; private set; }

    /// <summary>The font scale the editor shows the text at (a shrunk-to-fit shape's text is shown that size).</summary>
    public double Scale { get; private set; } = 1;

    public bool IsEditing => Box != null;

    /// <summary>Edits the selected shape's text (the caret at <paramref name="caretAt"/>, else all text selected).</summary>
    public void Begin(Point? caretAt = null)
    {
        if (selection.PrimaryHit is { } h && TextContent.HasText(h.Element)) Begin(h.Element, h.Text, h.Bounds, caretAt);
    }

    /// <summary>The cells of a table as drawn: the cell element, its border and where it is on the slide.</summary>
    List<(OpenXmlElement Tc, Border B, Rect R)> CellsOf(OpenXmlElement frame)
    {
        var list = new List<(OpenXmlElement, Border, Rect)>();
        if (host.Drawing is not { } cv) return list;
        host.Surface.UpdateLayout();
        foreach (var g in cv.Children.OfType<Grid>())
            foreach (var b in g.Children.OfType<Border>())
                if (b.Tag is OpenXmlElement tc && tc.Ancestors().Contains(frame))
                    list.Add((tc, b, b.TransformToAncestor(host.Surface).TransformBounds(new Rect(b.RenderSize))));
        return list;
    }

    /// <summary>The table cell drawn at <paramref name="p"/>.</summary>
    public OpenXmlElement? CellAt(OpenXmlElement frame, Point p) => CellsOf(frame).Where(c => c.R.Contains(p)).Select(c => c.Tc).FirstOrDefault();

    /// <summary>Edits one table cell's text in place (its table becomes the selection).</summary>
    public bool EditCell(OpenXmlElement tc, Point? caretAt = null, (int From, int To)? paras = null)
    {
        var frame = tc.Ancestors().FirstOrDefault(a => a.LocalName == "graphicFrame");
        if (frame == null || CellsOf(frame).FirstOrDefault(c => c.Tc == tc) is not { B: not null } c) return false;
        selection.Primary = frame;
        selection.Cell = tc;
        adorner.Draw();
        Begin(tc, c.B.Child as FrameworkElement, c.R, caretAt, paras);
        return true;
    }

    /// <summary>Edits again after a paragraph setting, with the same paragraphs selected.</summary>
    public void ReEdit(OpenXmlElement el, (int From, int To) paras)
    {
        if (el.LocalName == "tc") { EditCell(el, null, paras); return; }
        selection.Primary = el;
        if (selection.PrimaryHit is { } h) Begin(h.Element, h.Text, h.Bounds, null, paras);
    }

    /// <summary>
    /// Edits the text of <paramref name="el"/> (a shape or a table cell) in place: the editor shows the text exactly as drawn
    /// (fonts, sizes, colors, bold, alignment...) on top of the hidden original. <paramref name="caretAt"/> (slide coordinates)
    /// places the caret, <paramref name="paras"/> selects those paragraphs, otherwise all text is selected.
    /// </summary>
    void Begin(OpenXmlElement el, FrameworkElement? panel, Rect bounds, Point? caretAt = null, (int From, int To)? paras = null)
    {
        double bt = 2 * host.Px;
        FlowDocument doc;
        Thickness pad = default;
        Point origin = bounds.TopLeft;
        if (panel is { IsVisible: true })
        {
            doc = SlideRenderer.EditDocument(panel);
            Scale = panel.Tag as double? ?? TextContent.FontScale(el); // shrunk to fit: the editor shows it that size
            pad = panel.Margin;
            origin = panel.TranslatePoint(new Point(-pad.Left, -pad.Top), host.Surface);
            origin.X = bounds.X; // the panel stretches to the shape's width
            hidden = panel;
            panel.Visibility = Visibility.Hidden;
        }
        else
        {
            doc = new FlowDocument { PagePadding = new Thickness(0) };
            Scale = 1;
            foreach (var line in TextContent.GetEditText(el).Split('\n'))
                doc.Blocks.Add(new Paragraph(new Run(line.Replace('\v', '\n'))) { Margin = default });
            doc.FontSize = (double.TryParse(TextFormat.FirstRun(el, "sz"), out var sz) ? sz / 100 : 18) * 96 / 72;
        }
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph { Margin = default });
        doc.FontFamily = new FontFamily("Malgun Gothic"); // paragraphs set their own; this is for an empty box

        var box = new RichTextBox
        {
            Document = doc, Tag = el,
            Width = bounds.Width + 2 * bt, MinHeight = bounds.Height + 2 * bt,
            Padding = pad, Background = Brushes.Transparent, AllowDrop = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            BorderBrush = host.Accent, BorderThickness = new Thickness(bt),
        };
        // plain-text paste; formatting goes through the ribbon and the user's shortcuts, other formatting is not kept
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
        Box = box;
        start = EditText(doc).Fmt;
        // picking a font, size, color or list setting moves focus to the boxes or a menu: the edit goes on
        // (only this editor: an earlier one, already closed, loses focus to the next one)
        box.LostKeyboardFocus += (_, e) => { if (Box == box && !host.IsFormatInput(e.NewFocus as DependencyObject) && !InMenu(e.NewFocus as DependencyObject)) Commit(); };
        box.SelectionChanged += (_, _) => { if (Box == box) host.SyncFormatUi(); };
        host.Marks.Children.Add(box);
        box.Focus();
        host.SyncFormatUi();
        if (caretAt is { } pt)
            host.Dispatcher.BeginInvoke(() =>
            {
                if (Box == box && box.GetPositionFromPoint(host.Surface.TranslatePoint(pt, box), true) is { } pos) box.CaretPosition = pos;
            }, DispatcherPriority.Loaded);
        else if (paras is { } pr && doc.Blocks.ToList() is var blocks && pr.To < blocks.Count)
            box.Selection.Select(blocks[pr.From].ContentStart, blocks[pr.To].ContentEnd);
        else box.SelectAll();
        host.Status(L.T("editingText"));
    }

    /// <summary>Closes the editor; unless <paramref name="cancel"/>, a change goes into the slide (with undo).</summary>
    public void Commit(bool cancel = false)
    {
        if (Box is not { } box) return;
        Box = null; // first: removing the box fires LostKeyboardFocus again
        host.Marks.Children.Remove(box);
        if (hidden != null) hidden.Visibility = Visibility.Visible;
        hidden = null;
        host.SlideStatus();
        host.SyncFormatUi();
        var el = (OpenXmlElement)box.Tag;
        var (text, fmt) = EditText(box.Document);
        if (cancel || (text == TextContent.GetEditText(el) && fmt.SequenceEqual(start))) return;
        host.Deck!.History.Snapshot(host.CurrentSlide!);
        TextContent.SetEditText(el, text, fmt, start, Scale);
        host.Changed();
    }

    /// <summary>Tab / Shift+Tab in a table cell: the next or previous cell; Tab in the last cell adds a row, as in PowerPoint.</summary>
    public void NextCell(OpenXmlElement tc, int step)
    {
        var frame = tc.Ancestors().First(a => a.LocalName == "graphicFrame");
        Commit();
        var cells = Tables.TableCells(frame);
        int i = cells.IndexOf(tc) + step;
        if (i >= cells.Count)
        {
            host.Deck!.History.Snapshot(host.CurrentSlide!);
            var first = Tables.InsertRow(frame, Tables.TableSize(frame).Rows - 1, below: true);
            host.Changed();
            EditCell(first);
            return;
        }
        EditCell(cells[Math.Max(0, i)]);
    }

    /// <summary>The element being edited (a shape or a table cell).</summary>
    public OpenXmlElement? Target => Box?.Tag as OpenXmlElement;

    /// <summary>Bold / italic / underline / strikethrough on the editor's selection (or for the text typed next), like PowerPoint.</summary>
    public void Toggle(string id) => Toggle(Box!.Selection, id);

    /// <summary>Applies a character property to the editor's selection and gives the editor focus back.</summary>
    public void Apply(DependencyProperty property, object? value)
    {
        Box!.Selection.ApplyPropertyValue(property, value);
        FocusBack();
    }

    /// <summary>The paragraphs the editor's selection touches (indices into the shape's or cell's paragraphs).</summary>
    public (int From, int To) SelectedParagraphs()
    {
        var box = Box!;
        var blocks = box.Document.Blocks.ToList();
        int i = box.Selection.Start.Paragraph is { } a ? blocks.IndexOf(a) : -1, j = box.Selection.End.Paragraph is { } b ? blocks.IndexOf(b) : -1;
        if (i < 0) i = 0;
        if (j < i) j = Math.Max(i, blocks.Count - 1);
        return (i, j);
    }

    /// <summary>Points of the editor's selection as stored in the file (null: mixed sizes).</summary>
    public double? SizeOf(TextRange sel) =>
        sel.GetPropertyValue(TextElement.FontSizeProperty) is double px ? Math.Round(px * 72 / 96 / Scale, 1) : null;

    /// <summary>The size shown for the editor's selection: where it starts when the sizes are mixed.</summary>
    public double SelectedSize => SizeOf(Box!.Selection) ?? SizeOf(new TextRange(Box.Selection.Start, Box.Selection.Start)) ?? 18;

    /// <summary>The face of the editor's selection ("" when mixed).</summary>
    public string SelectedFont => Box!.Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily f ? f.Source.Split(',')[0].Trim() : "";

    /// <summary>Focus back to the text editor after the font / size boxes or a menu, once it has closed.</summary>
    public void FocusBack()
    {
        var box = Box;
        host.Dispatcher.BeginInvoke(() => { if (Box == box) box?.Focus(); }, DispatcherPriority.Input);
        host.SyncFormatUi();
    }

    /// <summary>Focus went into one of the ribbon's drop-down menus (colors, spacing...).</summary>
    static bool InMenu(DependencyObject? d)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ContextMenu) return true;
        return false;
    }

    /// <summary>The editor's text in <see cref="TextContent.GetEditText"/> form, with each character's formatting.</summary>
    internal static (string Text, List<TextContent.Fmt> Fmt) EditText(FlowDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        var fmt = new List<TextContent.Fmt>();
        bool first = true;
        void Add(char c, TextContent.Fmt f = default) { sb.Append(c); fmt.Add(f); }
        // underline / strikethrough can sit on the run or any span / paragraph around it; weight, style and color are inherited
        static bool Deco(Run r, TextDecorationLocation loc)
        {
            for (DependencyObject? e = r; e is TextElement te; e = te.Parent)
                if ((e as Inline)?.TextDecorations?.Any(d => d.Location == loc) == true || (e as Paragraph)?.TextDecorations?.Any(d => d.Location == loc) == true)
                    return true;
            return false;
        }
        static string? Highlight(Run r)
        {
            for (DependencyObject? e = r; e is TextElement te; e = te.Parent)
                if (te.ReadLocalValue(TextElement.BackgroundProperty) != DependencyProperty.UnsetValue)
                    return te.Background is SolidColorBrush b ? Ooxml.Hex(b.Color) : null;
            return null;
        }
        void Inlines(InlineCollection inl)
        {
            foreach (var i in inl)
                switch (i)
                {
                    case Run r:
                        var f = new TextContent.Fmt(r.FontWeight >= FontWeights.Bold, r.FontStyle == FontStyles.Italic, Deco(r, TextDecorationLocation.Underline),
                            r.FontFamily?.Source, r.FontSize * 72 / 96, Deco(r, TextDecorationLocation.Strikethrough),
                            r.Foreground is SolidColorBrush fg ? Ooxml.Hex(fg.Color) : null, Highlight(r));
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
            case "underline": ToggleDeco(sel, TextDecorationLocation.Underline, TextDecorations.Underline); break;
            case "strike": ToggleDeco(sel, TextDecorationLocation.Strikethrough, TextDecorations.Strikethrough); break;
        }
    }

    /// <summary>Turns one decoration on or off and keeps the other (underline and strikethrough together).</summary>
    static void ToggleDeco(TextRange sel, TextDecorationLocation loc, TextDecorationCollection deco)
    {
        var cur = sel.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        bool on = cur?.Any(d => d.Location == loc) == true;
        var next = new TextDecorationCollection(cur?.Where(d => d.Location != loc) ?? []);
        if (!on) foreach (var d in deco) next.Add(d);
        sel.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
    }
}
