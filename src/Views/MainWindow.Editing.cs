using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DocumentFormat.OpenXml;
using Microsoft.Win32;
using A = DocumentFormat.OpenXml.Drawing;

namespace PptSlider;

/// <summary>Selection, move / resize / rotate, in-place text editing (shapes and table cells), formatting, inserting and deleting, undo / redo.</summary>
public partial class MainWindow
{
    // ---------- selection ----------

    /// <summary>The selected shapes (top-level shapes of the current slide); the last one is the one formatting reads from.</summary>
    readonly List<OpenXmlElement> sel = [];

    OpenXmlElement? selected
    {
        get => sel.Count > 0 ? sel[^1] : null;
        set { sel.Clear(); if (value != null) sel.Add(value); }
    }

    ShapeHit? SelectedHit => hits.FirstOrDefault(h => h.Element == selected);
    List<ShapeHit> SelectedHits => hits.Where(h => sel.Contains(h.Element)).ToList();
    internal OpenXmlElement? SelectedElement => selected;
    internal IReadOnlyList<OpenXmlElement> Selection => sel;

    enum Drag { None, Move, Resize, Rotate, Marquee, Draw }
    Drag drag;
    Point dragStart, dragEnd;
    Rect dragOrig, dragRect;
    Vector dragMove;
    double dragAngle;
    /// <summary>The resize handle being dragged: which edges move (-1 left / top, 1 right / bottom, 0 neither).</summary>
    (int X, int Y) handle;
    /// <summary>The table cell last clicked or edited: where rows and columns go in.</summary>
    OpenXmlElement? cell;
    /// <summary>A shape picked in the Shapes gallery, drawn by dragging on the slide next.</summary>
    string? pendingShape;

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

    internal double PxForTest => Px;

    static readonly (int X, int Y)[] Handles = [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];
    static Point HandlePoint(Rect r, (int X, int Y) h) => new(r.X + r.Width * (h.X + 1) / 2, r.Y + r.Height * (h.Y + 1) / 2);
    Point RotateHandle(Rect r) => new(r.X + r.Width / 2, r.Y - 22 * Px);

    /// <summary><paramref name="p"/> turned <paramref name="degrees"/> clockwise around the middle of <paramref name="r"/>.</summary>
    static Point Turn(Point p, Rect r, double degrees) =>
        degrees == 0 ? p : new RotateTransform(degrees, r.X + r.Width / 2, r.Y + r.Height / 2).Transform(p);

    /// <summary>The topmost shape at <paramref name="p"/> (a few pixels of slack, so thin lines can be picked).</summary>
    ShapeHit? HitAt(Point p)
    {
        double t = 4 * Px;
        return hits.LastOrDefault(h =>
        {
            var b = h.Bounds;
            b.Inflate(t, t);
            return b.Contains(Turn(p, h.Bounds, -Deck.Rotation(h.Element)));
        });
    }

    void DrawSelection()
    {
        foreach (var c in Overlay.Children.OfType<Shape>().ToList()) Overlay.Children.Remove(c);
        double px = Px;
        var accent = (Brush)FindResource("Accent");
        Shape Add(Shape s, double x, double y) { s.IsHitTestVisible = false; Canvas.SetLeft(s, x); Canvas.SetTop(s, y); Overlay.Children.Add(s); return s; }

        if (drag == Drag.Marquee || drag == Drag.Draw)
        {
            if (drag == Drag.Draw && pendingShape is "line" or "arrow")
                Add(new Line { X1 = dragStart.X, Y1 = dragStart.Y, X2 = dragEnd.X, Y2 = dragEnd.Y, Stroke = accent, StrokeThickness = 2 * px }, 0, 0);
            else
                Add(new Rectangle { Width = dragRect.Width, Height = dragRect.Height, Stroke = accent, StrokeThickness = 1 * px, StrokeDashArray = [4, 2],
                    Fill = new SolidColorBrush(Color.FromArgb(0x18, 0xC4, 0x3E, 0x1C)) }, dragRect.X, dragRect.Y);
            return;
        }
        var shown = SelectedHits;
        bool single = shown.Count == 1;
        foreach (var h in shown)
        {
            var r = h.Bounds;
            double rot = Deck.Rotation(h.Element);
            if (drag == Drag.Move) r.Offset(dragMove);
            if (single && drag == Drag.Resize) r = dragRect;
            if (single && drag == Drag.Rotate) rot = dragAngle;
            Add(new Rectangle
            {
                Width = r.Width, Height = r.Height, Stroke = accent, StrokeThickness = 1.5 * px, StrokeDashArray = [4, 2],
                RenderTransform = rot != 0 ? new RotateTransform(rot, r.Width / 2, r.Height / 2) : null,
            }, r.X, r.Y);
            if (!single) continue;
            foreach (var hd in Handles)
            {
                var c = Turn(HandlePoint(r, hd), r, rot);
                Add(new Rectangle { Width = 9 * px, Height = 9 * px, Fill = Brushes.White, Stroke = accent, StrokeThickness = 1.5 * px }, c.X - 4.5 * px, c.Y - 4.5 * px);
            }
            if (Deck.CanRotate(h.Element))
            {
                var c = Turn(RotateHandle(r), r, rot);
                Add(new Ellipse { Width = 11 * px, Height = 11 * px, Fill = Brushes.White, Stroke = accent, StrokeThickness = 1.5 * px }, c.X - 5.5 * px, c.Y - 5.5 * px);
            }
        }
    }

    /// <summary>The self-test drives the slide itself: real mouse events (capture sends one from wherever the cursor is) are ignored.</summary>
    internal bool SyntheticInput;

    void OnStageDown(object sender, MouseButtonEventArgs e)
    {
        if (SyntheticInput || editBox?.IsMouseOver == true) return;
        if (StageDown(e.GetPosition(Stage), Keyboard.Modifiers, e.ClickCount)) e.Handled = true;
    }

    void OnStageMove(object sender, MouseEventArgs e) { if (!SyntheticInput) StageMove(e.GetPosition(Stage), Keyboard.Modifiers); }
    void OnStageUp(object sender, MouseButtonEventArgs e) { if (!SyntheticInput) StageUp(); }

    /// <summary>A press on the slide at <paramref name="p"/> (slide units); true when it was used. The mouse handlers and the self-test come here.</summary>
    internal bool StageDown(Point p, ModifierKeys mods, int clicks)
    {
        if (Deck == null || Pen.Tool != Tool.Select) return false;
        CommitEdit();
        double px = Px;
        bool add = (mods & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
        dragStart = dragEnd = p;
        dragMove = default;
        clickedIn = null;
        bool Begin(Drag d) { drag = d; Stage.CaptureMouse(); DrawSelection(); return true; }

        if (pendingShape != null) { dragRect = new Rect(p, p); return Begin(Drag.Draw); }
        if (painter != null)
        {
            if (HitAt(p) is { } target) PaintFormat(target.Element); else EndPainter();
            return true;
        }
        if (SelectedHits is [var cur])
        {
            double rot = Deck.Rotation(cur.Element);
            if (Deck.CanRotate(cur.Element) && (p - Turn(RotateHandle(cur.Bounds), cur.Bounds, rot)).Length < 8 * px)
            {
                dragAngle = rot;
                dragOrig = cur.Bounds;
                return Begin(Drag.Rotate);
            }
            foreach (var hd in Handles)
                if ((p - Turn(HandlePoint(cur.Bounds, hd), cur.Bounds, rot)).Length < 7 * px)
                {
                    handle = hd;
                    dragOrig = dragRect = cur.Bounds;
                    return Begin(Drag.Resize);
                }
        }
        var hit = HitAt(p);
        if (hit == null)
        {
            if (!add) selected = null;
            cell = null;
            SyncFormatUi();
            dragRect = new Rect(p, p);
            return Begin(Drag.Marquee); // drag a box around shapes to select them
        }
        if (add)
        {
            if (!sel.Remove(hit.Element)) sel.Add(hit.Element);
            SyncFormatUi();
            DrawSelection();
            return true;
        }
        if (!sel.Contains(hit.Element)) selected = hit.Element; // a click on one of several selected keeps them all (to move together)
        cell = Deck.IsTable(hit.Element) ? CellAt(hit.Element, p) : null;
        SyncFormatUi();
        if (clicks == 2)
        {
            if (Deck.HasText(hit.Element)) { selected = hit.Element; BeginEdit(p); return false; }
            if (cell != null) { selected = hit.Element; EditCell(cell, p); return false; }
        }
        clickedIn = hit.Element;
        return Begin(Drag.Move);
    }

    /// <summary>The shape pressed on to move; a plain click (no drag) on one of several selected then selects just it.</summary>
    OpenXmlElement? clickedIn;

    internal void StageMove(Point p, ModifierKeys mods)
    {
        if (drag == Drag.None) return;
        var d = p - dragStart;
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        switch (drag)
        {
            case Drag.Move: dragMove = d; break;
            case Drag.Marquee: dragRect = new Rect(dragStart, p); break;
            case Drag.Draw:
                if (shift && pendingShape is not ("line" or "arrow")) // Shift: a square / circle
                {
                    double side = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
                    p = dragStart + new Vector(d.X < 0 ? -side : side, d.Y < 0 ? -side : side);
                }
                dragEnd = p;
                dragRect = new Rect(dragStart, p);
                break;
            case Drag.Resize: dragRect = Resized(d, shift ^ selected?.LocalName == "pic"); break; // pictures keep their shape unless Shift
            case Drag.Rotate:
                var c = new Point(dragOrig.X + dragOrig.Width / 2, dragOrig.Y + dragOrig.Height / 2);
                double a = Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI + 90;
                if (shift) a = Math.Round(a / 15) * 15;
                dragAngle = (a % 360 + 360) % 360;
                break;
        }
        DrawSelection();
    }

    /// <summary>The shape's new box while a handle is dragged by <paramref name="d"/>; corners keep the proportions when asked.</summary>
    Rect Resized(Vector d, bool keepAspect)
    {
        var o = dragOrig;
        if (selected != null && Deck.Rotation(selected) is var rot && rot != 0)
            d = (Vector)new RotateTransform(-rot).Transform((Point)d); // into the shape's own axes
        double l = o.Left, t = o.Top, r = o.Right, b = o.Bottom;
        if (handle.X < 0) l += d.X; else if (handle.X > 0) r += d.X;
        if (handle.Y < 0) t += d.Y; else if (handle.Y > 0) b += d.Y;
        double min = selected?.LocalName == "cxnSp" ? 0 : 8;
        if (r - l < min) { if (handle.X < 0) l = r - min; else r = l + min; }
        if (b - t < min) { if (handle.Y < 0) t = b - min; else b = t + min; }
        if (keepAspect && handle.X != 0 && handle.Y != 0 && o.Width > 0 && o.Height > 0)
        {
            double s = Math.Max((r - l) / o.Width, (b - t) / o.Height);
            if (handle.X < 0) l = r - o.Width * s; else r = l + o.Width * s;
            if (handle.Y < 0) t = b - o.Height * s; else b = t + o.Height * s;
        }
        return new Rect(l, t, r - l, b - t);
    }

    internal void StageUp()
    {
        if (drag == Drag.None) return;
        var was = drag;
        drag = Drag.None;
        Stage.ReleaseMouseCapture();
        switch (was)
        {
            case Drag.Marquee:
                if (dragRect.Width > 2 || dragRect.Height > 2)
                    foreach (var h in hits.Where(h => dragRect.Contains(h.Bounds) && !sel.Contains(h.Element))) sel.Add(h.Element);
                SyncFormatUi();
                DrawSelection();
                return;
            case Drag.Draw:
                DrawPendingShape(dragStart, dragEnd);
                return;
            case Drag.Move:
                if (dragMove.Length < 0.5)
                {
                    if (sel.Count > 1 && clickedIn != null) { selected = clickedIn; SyncFormatUi(); }
                    DrawSelection();
                    return;
                }
                Deck!.Snapshot(CurrentSlide!);
                foreach (var h in SelectedHits) Deck.SetBounds(h.Element, Rect.Offset(h.Bounds, dragMove));
                break;
            case Drag.Resize:
                if (selected == null || (dragRect.TopLeft - dragOrig.TopLeft).Length < 0.5 && (dragRect.BottomRight - dragOrig.BottomRight).Length < 0.5) { DrawSelection(); return; }
                Deck!.Snapshot(CurrentSlide!);
                Deck.SetBounds(selected, dragRect);
                break;
            case Drag.Rotate:
                if (selected == null || Math.Abs(dragAngle - Deck.Rotation(selected)) < 0.01) { DrawSelection(); return; }
                Deck!.Snapshot(CurrentSlide!);
                Deck.SetRotation(selected, dragOrig, dragAngle);
                break;
        }
        dragMove = default;
        Changed();
    }

    /// <summary>Adds the shape picked in the gallery over the dragged box (a plain click: one inch at that point).</summary>
    internal void DrawPendingShape(Point a, Point b)
    {
        if (Deck == null || pendingShape is not { } kind) return;
        pendingShape = null;
        Stage.Cursor = null;
        bool line = kind is "line" or "arrow";
        var r = new Rect(a, b);
        if (r.Width < 3 && r.Height < 3) { r = new Rect(a.X, a.Y, 96, line ? 0 : 96); b = r.BottomRight; }
        Deck.Snapshot(CurrentSlide!);
        selected = Deck.AddShape(CurrentSlide!, kind, r, line && b.X < a.X, line && b.Y < a.Y);
        Changed();
        UpdateStatus();
    }

    // ---------- in-place text editing ----------

    /// <summary>Edits the selected shape's text (the caret at <paramref name="caretAt"/>, else all text selected).</summary>
    void BeginEdit(Point? caretAt = null)
    {
        if (SelectedHit is { } h && Deck.HasText(h.Element)) BeginEdit(h.Element, h.Text, h.Bounds, caretAt);
    }

    /// <summary>The cells of a table as drawn: the cell element, its border and where it is on the slide.</summary>
    List<(OpenXmlElement Tc, Border B, Rect R)> CellsOf(OpenXmlElement frame)
    {
        var list = new List<(OpenXmlElement, Border, Rect)>();
        if (SlideHost.Content is not Canvas cv) return list;
        Stage.UpdateLayout();
        foreach (var g in cv.Children.OfType<Grid>())
            foreach (var b in g.Children.OfType<Border>())
                if (b.Tag is OpenXmlElement tc && tc.Ancestors().Contains(frame))
                    list.Add((tc, b, b.TransformToAncestor(Stage).TransformBounds(new Rect(b.RenderSize))));
        return list;
    }

    OpenXmlElement? CellAt(OpenXmlElement frame, Point p) => CellsOf(frame).Where(c => c.R.Contains(p)).Select(c => c.Tc).FirstOrDefault();

    /// <summary>Edits one table cell's text in place (its table becomes the selection).</summary>
    internal bool EditCell(OpenXmlElement tc, Point? caretAt = null, (int From, int To)? paras = null)
    {
        var frame = tc.Ancestors().FirstOrDefault(a => a.LocalName == "graphicFrame");
        if (frame == null || CellsOf(frame).FirstOrDefault(c => c.Tc == tc) is not { B: not null } c) return false;
        selected = frame;
        cell = tc;
        DrawSelection();
        BeginEdit(tc, c.B.Child as FrameworkElement, c.R, caretAt, paras);
        return true;
    }

    /// <summary>Edits again after a paragraph setting, with the same paragraphs selected.</summary>
    void ReEdit(OpenXmlElement el, (int From, int To) paras)
    {
        if (el.LocalName == "tc") { EditCell(el, null, paras); return; }
        selected = el;
        if (SelectedHit is { } h) BeginEdit(h.Element, h.Text, h.Bounds, null, paras);
    }

    /// <summary>
    /// Edits the text of <paramref name="el"/> (a shape or a table cell) in place: the editor shows the text exactly as drawn
    /// (fonts, sizes, colors, bold, alignment...) on top of the hidden original. <paramref name="caretAt"/> (slide coordinates)
    /// places the caret, <paramref name="paras"/> selects those paragraphs, otherwise all text is selected.
    /// </summary>
    void BeginEdit(OpenXmlElement el, FrameworkElement? panel, Rect bounds, Point? caretAt = null, (int From, int To)? paras = null)
    {
        double bt = 2 * Px;
        FlowDocument doc;
        Thickness pad = default;
        Point origin = bounds.TopLeft;
        if (panel is { IsVisible: true })
        {
            doc = SlideRenderer.EditDocument(panel);
            editScale = panel.Tag as double? ?? Deck.FontScale(el); // shrunk to fit: the editor shows it that size
            pad = panel.Margin;
            origin = panel.TranslatePoint(new Point(-pad.Left, -pad.Top), Stage);
            origin.X = bounds.X; // the panel stretches to the shape's width
            editHidden = panel;
            panel.Visibility = Visibility.Hidden;
        }
        else
        {
            doc = new FlowDocument { PagePadding = new Thickness(0) };
            editScale = 1;
            foreach (var line in Deck.GetEditText(el).Split('\n'))
                doc.Blocks.Add(new Paragraph(new Run(line.Replace('\v', '\n'))) { Margin = default });
            doc.FontSize = (double.TryParse(Deck.FirstRun(el, "sz"), out var sz) ? sz / 100 : 18) * 96 / 72;
        }
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph { Margin = default });
        doc.FontFamily = new FontFamily("Malgun Gothic"); // paragraphs set their own; this is for an empty box

        var box = new RichTextBox
        {
            Document = doc, Tag = el,
            Width = bounds.Width + 2 * bt, MinHeight = bounds.Height + 2 * bt,
            Padding = pad, Background = Brushes.Transparent, AllowDrop = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            BorderBrush = (Brush)FindResource("Accent"), BorderThickness = new Thickness(bt),
        };
        // plain-text paste; formatting goes through the ribbon and the user's shortcuts (OnKey), other formatting is not kept
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
        // picking a font, size, color or list setting moves focus to the boxes or a menu: the edit goes on
        // (only this editor: an earlier one, already closed, loses focus to the next one)
        box.LostKeyboardFocus += (_, e) => { if (editBox == box && !InFormatBoxes(e.NewFocus as DependencyObject) && !InMenu(e.NewFocus as DependencyObject)) CommitEdit(); };
        box.SelectionChanged += (_, _) => { if (editBox == box) SyncFormatUi(); };
        Overlay.Children.Add(box);
        box.Focus();
        SyncFormatUi();
        if (caretAt is { } pt)
            Dispatcher.BeginInvoke(() =>
            {
                if (editBox == box && box.GetPositionFromPoint(Stage.TranslatePoint(pt, box), true) is { } pos) box.CaretPosition = pos;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        else if (paras is { } pr && doc.Blocks.ToList() is var blocks && pr.To < blocks.Count)
            box.Selection.Select(blocks[pr.From].ContentStart, blocks[pr.To].ContentEnd);
        else box.SelectAll();
        StatusText.Text = L.T("editingText");
    }

    /// <summary>The editor's text in <see cref="Deck.GetEditText"/> form, with each character's formatting.</summary>
    internal static (string Text, List<Deck.Fmt> Fmt) EditText(FlowDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        var fmt = new List<Deck.Fmt>();
        bool first = true;
        void Add(char c, Deck.Fmt f = default) { sb.Append(c); fmt.Add(f); }
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
                    return te.Background is SolidColorBrush b ? Deck.Hex(b.Color) : null;
            return null;
        }
        void Inlines(InlineCollection inl)
        {
            foreach (var i in inl)
                switch (i)
                {
                    case Run r:
                        var f = new Deck.Fmt(r.FontWeight >= FontWeights.Bold, r.FontStyle == FontStyles.Italic, Deco(r, TextDecorationLocation.Underline),
                            r.FontFamily?.Source, r.FontSize * 72 / 96, Deco(r, TextDecorationLocation.Strikethrough),
                            r.Foreground is SolidColorBrush fg ? Deck.Hex(fg.Color) : null, Highlight(r));
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

    /// <summary>Bold / italic / underline / strikethrough on the editor's selection (or for the text typed next), like PowerPoint.</summary>
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

    /// <summary>Tab / Shift+Tab in a table cell: the next or previous cell; Tab in the last cell adds a row, as in PowerPoint.</summary>
    internal void NextCell(OpenXmlElement tc, int step)
    {
        var frame = tc.Ancestors().First(a => a.LocalName == "graphicFrame");
        CommitEdit();
        var cells = Deck.TableCells(frame);
        int i = cells.IndexOf(tc) + step;
        if (i >= cells.Count)
        {
            Deck!.Snapshot(CurrentSlide!);
            var first = Deck.InsertRow(frame, Deck.TableSize(frame).Rows - 1, below: true);
            Changed();
            EditCell(first);
            return;
        }
        EditCell(cells[Math.Max(0, i)]);
    }

    // ---------- formatting ----------

    /// <summary>Runs an edit on every selected shape with text (or table) with undo + redraw.</summary>
    void EditSelected(Action<OpenXmlElement> f)
    {
        CommitEdit();
        var targets = sel.Where(Deck.HasFormat).ToList();
        if (Deck == null || targets.Count == 0)
        {
            StatusText.Text = L.T("selectTextFirst");
            return;
        }
        Deck.Snapshot(CurrentSlide!);
        foreach (var t in targets) f(t);
        Changed();
    }

    /// <summary>The paragraphs the editor's selection touches (indices into the shape's or cell's paragraphs).</summary>
    static (int From, int To) EditorParagraphs(RichTextBox box)
    {
        var blocks = box.Document.Blocks.ToList();
        int i = box.Selection.Start.Paragraph is { } a ? blocks.IndexOf(a) : -1, j = box.Selection.End.Paragraph is { } b ? blocks.IndexOf(b) : -1;
        if (i < 0) i = 0;
        if (j < i) j = Math.Max(i, blocks.Count - 1);
        return (i, j);
    }

    /// <summary>
    /// A paragraph setting (bullets, indent, spacing, alignment) on the paragraphs being edited, or else on every paragraph
    /// of the selected shapes. <paramref name="f"/> gets each paragraph with the shape or cell it belongs to.
    /// </summary>
    void ParagraphOp(Action<List<(A.Paragraph P, OpenXmlElement Owner)>> f)
    {
        if (editBox is { } box)
        {
            var el = (OpenXmlElement)box.Tag;
            var (from, to) = EditorParagraphs(box);
            CommitEdit();
            Deck!.Snapshot(CurrentSlide!);
            f(Deck.Paragraphs(el).Skip(from).Take(to - from + 1).Select(p => (p, el)).ToList());
            Changed();
            ReEdit(el, (from, to));
            return;
        }
        EditSelected(sh => f(Deck.Paragraphs(sh).Select(p => (p, p.Ancestors().FirstOrDefault(a => a.LocalName == "tc") ?? sh)).ToList()));
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

    internal void SetTextColor(Color c)
    {
        if (editBox is { } box)
        {
            box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(c));
            BackToEditor();
        }
        else EditSelected(sh => Deck.EditRuns(sh, r => Deck.SetColor(r, c)));
    }

    internal void SetHighlight(Color? c)
    {
        if (editBox is { } box)
        {
            box.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, c is { } v ? new SolidColorBrush(v) : null);
            BackToEditor();
        }
        else EditSelected(sh => Deck.EditRuns(sh, r => Deck.SetHighlight(r, c)));
    }

    /// <summary>Focus back to the text editor after the font / size boxes or a menu, once it has closed.</summary>
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

    /// <summary>Focus went into one of the ribbon's drop-down menus (colors, spacing...).</summary>
    static bool InMenu(DependencyObject? d)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ContextMenu) return true;
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
            SizeBox.Text = Deck.HasFormat(selected) ? SelectedSize.ToString() : "";
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
    void OnStrike(object? s, RoutedEventArgs? e) { if (editBox != null) ToggleInEditor("strike"); else ToggleRun("strike", "sngStrike", "noStrike"); }
    void OnAlign(object s, RoutedEventArgs e) => SetAlign((string)((Button)s).CommandParameter);
    internal void SetAlign(string algn) => ParagraphOp(ps => ps.ForEach(x => Deck.SetAlign(x.P, algn)));

    /// <summary>Bullets ("buChar") or numbering ("buAutoNum") on, or off when every paragraph concerned has them already.</summary>
    internal void ToggleBullets(string kind) => ParagraphOp(ps =>
    {
        bool all = ps.Count > 0 && ps.All(x => Deck!.Renderer.BulletKind(CurrentSlide!, x.Owner, x.P) == kind);
        foreach (var x in ps) Deck.SetBullet(x.P, all ? null : kind);
    });

    internal void IndentParagraphs(int delta) => ParagraphOp(ps => ps.ForEach(x => Deck.Indent(x.P, delta)));
    internal void SetLineSpacing(double multiple) => ParagraphOp(ps => ps.ForEach(x => Deck.SetLineSpacing(x.P, multiple)));

    void OnBullets(object s, RoutedEventArgs e) => ToggleBullets("buChar");
    void OnNumbering(object s, RoutedEventArgs e) => ToggleBullets("buAutoNum");
    void OnIndentMore(object? s, RoutedEventArgs? e) => IndentParagraphs(1);
    void OnIndentLess(object? s, RoutedEventArgs? e) => IndentParagraphs(-1);

    void OnLineSpacing(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        foreach (var m in new[] { 1.0, 1.15, 1.5, 2.0, 2.5, 3.0 })
        {
            var item = new MenuItem { Header = m.ToString("0.0#") };
            item.Click += (_, _) => SetLineSpacing(m);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>A drop-down of color swatches under <paramref name="target"/>; <paramref name="none"/> adds a "no color" entry.</summary>
    ContextMenu ColorMenu(UIElement target, IEnumerable<Color> colors, Action<Color?> pick, string? none = null)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        var panel = new WrapPanel { Width = 168 };
        foreach (var c in colors)
        {
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(2), Background = new SolidColorBrush(c), BorderBrush = Brushes.Gray, Focusable = false };
            b.Click += (_, _) => { menu.IsOpen = false; pick(c); };
            panel.Children.Add(b);
        }
        menu.Items.Add(panel);
        if (none != null)
        {
            var item = new MenuItem { Header = none };
            item.Click += (_, _) => pick(null);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
        return menu;
    }

    void OnFontColor(object sender, RoutedEventArgs e) => ColorMenu(ColorBtn, TextColors, c => { if (c is { } v) SetTextColor(v); });

    static readonly Color[] HighlightColors =
    [
        Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0x00, 0xFF, 0x00), Color.FromRgb(0x00, 0xFF, 0xFF), Color.FromRgb(0xFF, 0x00, 0xFF),
        Color.FromRgb(0x00, 0x00, 0xFF), Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0x00, 0x00, 0x80), Color.FromRgb(0x00, 0x80, 0x80),
        Color.FromRgb(0x00, 0x80, 0x00), Color.FromRgb(0x80, 0x00, 0x80), Color.FromRgb(0x80, 0x00, 0x00), Color.FromRgb(0x80, 0x80, 0x00),
        Color.FromRgb(0x80, 0x80, 0x80), Color.FromRgb(0xC0, 0xC0, 0xC0), Colors.Black,
    ];

    void OnHighlight(object sender, RoutedEventArgs e) => ColorMenu((UIElement)sender, HighlightColors, SetHighlight, L.T("noHighlight"));

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

    /// <summary>A picture on the clipboard (copied image, or an image file copied in Explorer) goes on the slide.</summary>
    internal bool PastePicture()
    {
        if (Deck == null) return false;
        try
        {
            if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(f => Deck.PictureType(System.IO.Path.GetExtension(f)) != null) is { } f)
                return InsertPictureFile(f);
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } img) return false;
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return InsertPicture(ms.ToArray(), Deck.PictureType(".png")!.Value);
        }
        catch (System.Runtime.InteropServices.ExternalException) { return false; } // the clipboard is busy
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
        if (Deck == null || sel.Count == 0) return;
        CommitEdit();
        Deck.Snapshot(CurrentSlide!);
        foreach (var el in sel) el.Remove();
        selected = null;
        cell = null;
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
        Restored(part);
    }

    void OnRedo(object? sender, RoutedEventArgs? e)
    {
        CommitEdit();
        if (Deck?.Redo() is not { } part) { StatusText.Text = L.T("nothingToRedo"); return; }
        Restored(part);
    }

    /// <summary>After undo / redo replaced a slide: show it (its shapes are new objects, so nothing stays selected).</summary>
    void Restored(DocumentFormat.OpenXml.Packaging.SlidePart part)
    {
        selected = null;
        cell = null;
        int i = Deck!.Slides.IndexOf(part);
        if (Thumbs.Items[i] is ListBoxItem item) item.Content = ThumbContent(i);
        Current = -1;
        Go(i);
        UpdateTitle();
    }
}
