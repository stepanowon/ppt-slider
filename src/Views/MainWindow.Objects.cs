using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>Working with shapes: clipboard, duplicate, arrange / align, fill / outline / rotate, the Shapes gallery, tables, format painter.</summary>
public partial class MainWindow
{
    // ---------- clipboard ----------

    /// <summary>Shapes copied with Ctrl+C / Ctrl+X; the system clipboard gets a token naming them (and their text).</summary>
    Deck.Clip? clip;
    string clipToken = "";
    bool clipCut;
    int pasteCount;
    const string ClipFormat = "PptSlider.Shapes";

    internal void CopyShapes(bool cut)
    {
        if (Deck == null || sel.Count == 0) { StatusText.Text = L.T("selectShapeFirst"); return; }
        CommitEdit();
        var picked = SelectedHits;
        clip = Deck.Copy(CurrentSlide!, picked.Select(h => (h.Element, h.Bounds)));
        clipToken = Guid.NewGuid().ToString();
        clipCut = cut;
        pasteCount = 0;
        var text = string.Join("\r\n", picked.SelectMany(h => Deck.Paragraphs(h.Element)).Select(p => p.InnerText).Where(t => t.Length > 0));
        try
        {
            var data = new DataObject();
            data.SetData(ClipFormat, clipToken);
            if (text.Length > 0) data.SetText(text);
            Clipboard.SetDataObject(data, true);
        }
        catch (System.Runtime.InteropServices.ExternalException) { } // the clipboard is busy: pasting here still works
        if (!cut) return;
        Deck.Snapshot(CurrentSlide!);
        foreach (var h in picked) h.Element.Remove();
        selected = null;
        Changed();
    }

    // the ribbon buttons: while text is being edited they work on the text, as Ctrl+C / X / V do there
    void OnCut(object s, RoutedEventArgs e) { if (editBox != null) ApplicationCommands.Cut.Execute(null, editBox); else CopyShapes(true); }
    void OnCopy(object s, RoutedEventArgs e) { if (editBox != null) ApplicationCommands.Copy.Execute(null, editBox); else CopyShapes(false); }
    void OnPaste(object s, RoutedEventArgs e)
    {
        if (editBox != null) ApplicationCommands.Paste.Execute(null, editBox);
        else if (!Paste()) StatusText.Text = L.T("nothingToPaste");
    }

    /// <summary>Ctrl+V: shapes copied here, else a picture, else text (as a new text box).</summary>
    internal bool Paste()
    {
        if (Deck == null) return false;
        string? token = null, text = null;
        try
        {
            if (Clipboard.ContainsData(ClipFormat)) token = Clipboard.GetData(ClipFormat) as string;
            else if (Clipboard.ContainsText()) text = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException) { token = clipToken; } // busy: assume it is still ours
        if (clip != null && token == clipToken) return PasteShapes();
        if (PastePicture()) return true;
        if (!string.IsNullOrWhiteSpace(text))
        {
            CommitEdit();
            Pen.SetTool(Tool.Select);
            double w = Deck.Renderer.Width, h = Deck.Renderer.Height;
            Deck.Snapshot(CurrentSlide!);
            selected = Deck.AddTextBox(CurrentSlide!, new Rect(w * 0.25, h * 0.4, w * 0.5, h * 0.12), "");
            Deck.SetText(selected, text.Replace("\r\n", "\n").TrimEnd('\n'));
            Changed();
            return true;
        }
        return clip != null && PasteShapes(); // nothing else usable: what was copied here
    }

    /// <summary>Pastes the copied shapes; on their own slide each paste lands a little lower right, as in PowerPoint.</summary>
    bool PasteShapes()
    {
        if (Deck == null || clip == null) return false;
        CommitEdit();
        int n = pasteCount++;
        double off = clip.From == CurrentSlide ? (clipCut ? n : n + 1) * 10 : 0;
        Deck.Snapshot(CurrentSlide!);
        var added = Deck.Paste(CurrentSlide!, clip, off, off);
        if (added.Count == 0) { StatusText.Text = L.T("nothingToPaste"); return false; }
        Pen.SetTool(Tool.Select);
        sel.Clear();
        sel.AddRange(added);
        Changed();
        return true;
    }

    /// <summary>Ctrl+D with shapes selected: copies of them right next to them.</summary>
    internal void DuplicateShapes()
    {
        if (Deck == null || sel.Count == 0) return;
        CommitEdit();
        var copy = Deck.Copy(CurrentSlide!, SelectedHits.Select(h => (h.Element, h.Bounds)));
        Deck.Snapshot(CurrentSlide!);
        var added = Deck.Paste(CurrentSlide!, copy, 10, 10);
        sel.Clear();
        sel.AddRange(added);
        Changed();
    }

    /// <summary>Selects these shapes, as clicking them with Shift does.</summary>
    internal void SelectShapes(params OpenXmlElement[] els)
    {
        CommitEdit();
        sel.Clear();
        sel.AddRange(els);
        SyncFormatUi();
        DrawSelection();
    }

    internal OpenXmlElement? CurrentCell => cell;
    internal System.Windows.Controls.RichTextBox? Editor => editBox;
    internal void EndEdit() => CommitEdit();

    internal void SelectAll()
    {
        if (Deck == null) return;
        CommitEdit();
        Pen.SetTool(Tool.Select);
        sel.Clear();
        sel.AddRange(hits.Select(h => h.Element));
        SyncFormatUi();
        DrawSelection();
    }

    /// <summary>Arrow keys move the selected shapes (Ctrl: one pixel).</summary>
    internal void Nudge(double dx, double dy)
    {
        if (Deck == null) return;
        Deck.Snapshot(CurrentSlide!);
        foreach (var h in SelectedHits) Deck.SetBounds(h.Element, Rect.Offset(h.Bounds, new Vector(dx, dy)));
        Changed();
    }

    // ---------- arrange, align, fill, outline, rotate ----------

    /// <summary>Runs <paramref name="f"/> on each selected shape it applies to (<paramref name="can"/>), with undo + redraw.</summary>
    void ShapeOp(Action<OpenXmlElement, Rect> f, Func<OpenXmlElement, bool>? can = null)
    {
        CommitEdit();
        var targets = SelectedHits.Where(h => (can ?? Deck.CanFormatShape)(h.Element)).ToList();
        if (Deck == null || targets.Count == 0) { StatusText.Text = L.T("selectShapeFirst"); return; }
        Deck.Snapshot(CurrentSlide!);
        foreach (var h in targets) f(h.Element, h.Bounds);
        Changed();
    }

    internal void Arrange(string how)
    {
        if (Deck == null || sel.Count == 0) { StatusText.Text = L.T("selectShapeFirst"); return; }
        CommitEdit();
        Deck.Snapshot(CurrentSlide!);
        Deck.Arrange(sel.ToList(), how);
        Changed();
    }

    /// <summary>Lines up the selected shapes: with each other when there are several, else on the slide.</summary>
    internal void AlignShapes(string how)
    {
        var shown = SelectedHits;
        if (Deck == null || shown.Count == 0) { StatusText.Text = L.T("selectShapeFirst"); return; }
        var f = shown.Count > 1 ? shown.Select(h => h.Bounds).Aggregate(Rect.Union) : new Rect(0, 0, Deck.Renderer.Width, Deck.Renderer.Height);
        ShapeOp((el, r) =>
        {
            double x = how switch { "left" => f.Left, "center" => f.Left + (f.Width - r.Width) / 2, "right" => f.Right - r.Width, _ => r.X };
            double y = how switch { "top" => f.Top, "middle" => f.Top + (f.Height - r.Height) / 2, "bottom" => f.Bottom - r.Height, _ => r.Y };
            Deck.SetBounds(el, new Rect(x, y, r.Width, r.Height));
        }, _ => true);
    }

    static readonly Color[] ShapeColors =
    [
        Colors.White, Colors.Black, Color.FromRgb(0xE7, 0xE6, 0xE6), Color.FromRgb(0x44, 0x54, 0x6A), Color.FromRgb(0x44, 0x72, 0xC4),
        Color.FromRgb(0xED, 0x7D, 0x31), Color.FromRgb(0xA5, 0xA5, 0xA5), Color.FromRgb(0xFF, 0xC0, 0x00), Color.FromRgb(0x5B, 0x9B, 0xD5),
        Color.FromRgb(0x70, 0xAD, 0x47), Color.FromRgb(0xC0, 0x00, 0x00), Colors.Red, Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0x92, 0xD0, 0x50),
        Color.FromRgb(0x00, 0xB0, 0x50), Color.FromRgb(0x00, 0xB0, 0xF0), Color.FromRgb(0x00, 0x70, 0xC0), Color.FromRgb(0x00, 0x20, 0x60),
        Color.FromRgb(0x70, 0x30, 0xA0), Color.FromRgb(0xDE, 0xEB, 0xF7), Color.FromRgb(0xFB, 0xE5, 0xD6),
    ];

    internal void SetShapeFill(Color? c) => ShapeOp((el, _) => Deck.SetFill(el, c), el => Deck.CanFormatShape(el) && el.LocalName != "cxnSp");
    internal void SetOutline(Color? c) => ShapeOp((el, _) => Deck.SetLineColor(el, c));
    internal void SetOutlineWidth(double pt) => ShapeOp((el, _) => Deck.SetLineWidth(el, pt));
    internal void Rotate(double by) => ShapeOp((el, r) => Deck.SetRotation(el, r, Deck.Rotation(el) + by), Deck.CanRotate);
    internal void Flip(bool horizontal) => ShapeOp((el, r) => Deck.Flip(el, r, horizontal), Deck.CanRotate);

    void OnShapeFill(object sender, RoutedEventArgs e) => ColorMenu((UIElement)sender, ShapeColors, SetShapeFill, L.T("noFill"));

    void OnShapeOutline(object sender, RoutedEventArgs e)
    {
        var menu = ColorMenu((UIElement)sender, ShapeColors, SetOutline, L.T("noOutline"));
        menu.Items.Add(new Separator());
        var weight = new MenuItem { Header = L.T("outlineWeight") };
        foreach (var pt in new[] { 0.25, 0.5, 0.75, 1, 1.5, 2.25, 3, 4.5, 6 })
        {
            var item = new MenuItem { Header = $"{pt} pt" };
            item.Click += (_, _) => SetOutlineWidth(pt);
            weight.Items.Add(item);
        }
        menu.Items.Add(weight);
        var dashes = new MenuItem { Header = L.T("outlineDash") };
        foreach (var (val, key) in new[] { ("solid", "dashSolid"), ("sysDot", "dashDot"), ("dash", "dashDash"), ("lgDashDot", "dashDashDot") })
        {
            var item = new MenuItem { Header = L.T(key) };
            item.Click += (_, _) => ShapeOp((el, _) => Deck.SetDash(el, val));
            dashes.Items.Add(item);
        }
        menu.Items.Add(dashes);
    }

    /// <summary>A plain drop-down menu of (text, action) under <paramref name="target"/>.</summary>
    static void Menu(object target, params (string Text, Action Do)[] items)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)target, Placement = PlacementMode.Bottom };
        foreach (var (text, act) in items)
        {
            if (text == "-") { menu.Items.Add(new Separator()); continue; }
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => act();
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void OnRotateMenu(object sender, RoutedEventArgs e) => Menu(sender,
        (L.T("rotateRight"), () => Rotate(90)), (L.T("rotateLeft"), () => Rotate(-90)), ("-", () => { }),
        (L.T("flipV"), () => Flip(false)), (L.T("flipH"), () => Flip(true)));

    void OnArrangeMenu(object sender, RoutedEventArgs e) => Menu(sender,
        (L.T("bringToFront"), () => Arrange("front")), (L.T("bringForward"), () => Arrange("forward")),
        (L.T("sendBackward"), () => Arrange("backward")), (L.T("sendToBack"), () => Arrange("back")), ("-", () => { }),
        (L.T("alignLeftObj"), () => AlignShapes("left")), (L.T("alignCenterObj"), () => AlignShapes("center")), (L.T("alignRightObj"), () => AlignShapes("right")),
        (L.T("alignTopObj"), () => AlignShapes("top")), (L.T("alignMiddleObj"), () => AlignShapes("middle")), (L.T("alignBottomObj"), () => AlignShapes("bottom")),
        ("-", () => { }), (L.T("rotateRight"), () => Rotate(90)), (L.T("rotateLeft"), () => Rotate(-90)), (L.T("flipV"), () => Flip(false)), (L.T("flipH"), () => Flip(true)));

    void OnFront(object s, RoutedEventArgs e) => Arrange("front");
    void OnBack(object s, RoutedEventArgs e) => Arrange("back");
    void OnForward(object s, RoutedEventArgs e) => Arrange("forward");
    void OnBackward(object s, RoutedEventArgs e) => Arrange("backward");
    void OnAlignMenu(object sender, RoutedEventArgs e) => Menu(sender,
        (L.T("alignLeftObj"), () => AlignShapes("left")), (L.T("alignCenterObj"), () => AlignShapes("center")), (L.T("alignRightObj"), () => AlignShapes("right")),
        ("-", () => { }),
        (L.T("alignTopObj"), () => AlignShapes("top")), (L.T("alignMiddleObj"), () => AlignShapes("middle")), (L.T("alignBottomObj"), () => AlignShapes("bottom")));

    // ---------- shapes gallery ----------

    void OnShapes(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        var panel = new WrapPanel { Width = 7 * 34 };
        foreach (var kind in Deck.ShapeKinds)
        {
            var icon = new Grid { Width = 22, Height = 22 };
            if (kind is "line" or "arrow")
            {
                icon.Children.Add(new Line { X1 = 3, Y1 = 19, X2 = 19, Y2 = 3, Stroke = Brushes.DimGray, StrokeThickness = 1.5 });
                if (kind == "arrow") icon.Children.Add(new Polygon { Points = [new(19, 3), new(12, 5), new(17, 10)], Fill = Brushes.DimGray });
            }
            else
                icon.Children.Add(new Path { Data = SlideRenderer.PresetGeometry(kind, 20, 20), Margin = new Thickness(1), Stroke = Brushes.DimGray, StrokeThickness = 1.2,
                    Fill = new SolidColorBrush(Color.FromRgb(0xDD, 0xE6, 0xF4)) });
            var b = new Button { Content = icon, Width = 32, Height = 30, Margin = new Thickness(1), ToolTip = L.T("shape." + kind), Focusable = false,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            b.Click += (_, _) => { menu.IsOpen = false; StartShape(kind); };
            panel.Children.Add(b);
        }
        menu.Items.Add(panel);
        menu.IsOpen = true;
    }

    /// <summary>The next drag on the slide draws <paramref name="kind"/> (Esc cancels).</summary>
    internal void StartShape(string kind)
    {
        if (Deck == null) return;
        CommitEdit();
        Pen.SetTool(Tool.Select);
        EndPainter();
        pendingShape = kind;
        Stage.Cursor = Cursors.Cross;
        StatusText.Text = L.T("drawShapeHint");
    }

    // ---------- tables ----------

    void OnInsertTable(object sender, RoutedEventArgs e)
    {
        const int rows = 8, cols = 10;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        var label = new TextBlock { Text = L.T("insertTable"), Margin = new Thickness(2, 0, 0, 4) };
        var grid = new UniformGrid { Rows = rows, Columns = cols };
        var boxes = new Border[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var b = boxes[r, c] = new Border { Width = 17, Height = 17, Margin = new Thickness(1), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White };
                int rr = r, cc = c;
                b.MouseEnter += (_, _) =>
                {
                    for (int i = 0; i < rows; i++)
                        for (int j = 0; j < cols; j++)
                            boxes[i, j].Background = i <= rr && j <= cc ? new SolidColorBrush(Color.FromRgb(0xF4, 0xC7, 0xB8)) : Brushes.White;
                    label.Text = L.F("tableSize", cc + 1, rr + 1);
                };
                b.MouseLeftButtonUp += (_, _) => { menu.IsOpen = false; InsertTable(rr + 1, cc + 1); };
                grid.Children.Add(b);
            }
        menu.Items.Add(new StackPanel { Children = { label, grid } });
        menu.IsOpen = true;
    }

    /// <summary>A table across 80% of the slide's width, selected, its first cell current.</summary>
    internal void InsertTable(int rows, int cols)
    {
        if (Deck == null) return;
        CommitEdit();
        Pen.SetTool(Tool.Select);
        double w = Deck.Renderer.Width, h = Deck.Renderer.Height;
        Deck.Snapshot(CurrentSlide!);
        selected = Deck.AddTable(CurrentSlide!, rows, cols, new Rect(w * 0.1, h * 0.25, w * 0.8, 0));
        cell = Deck.Cell(selected, 0, 0);
        Changed();
    }

    /// <summary>A row / column edit at the current cell of the selected table (its first cell when none was clicked).</summary>
    internal void TableOp(Action<OpenXmlElement, OpenXmlElement, int, int> f)
    {
        CommitEdit();
        if (Deck == null || selected is not { } frame || !Deck.IsTable(frame)) { StatusText.Text = L.T("selectTableFirst"); return; }
        var tc = cell != null && cell.Ancestors().Contains(frame) ? cell : Deck.Cell(frame, 0, 0);
        var (row, col) = Deck.CellIndex(tc);
        Deck.Snapshot(CurrentSlide!);
        f(frame, tc, row, col);
        if (frame.Parent == null) selected = null; // the table went with its last row / column
        if (cell?.Parent?.Parent == null) cell = null;
        Changed();
    }

    void OnRowAbove(object s, RoutedEventArgs e) => TableOp((f, _, r, _) => Deck.InsertRow(f, r, false));
    void OnRowBelow(object s, RoutedEventArgs e) => TableOp((f, _, r, _) => Deck.InsertRow(f, r, true));
    void OnColLeft(object s, RoutedEventArgs e) => TableOp((f, _, _, c) => Deck.InsertColumn(f, c, false));
    void OnColRight(object s, RoutedEventArgs e) => TableOp((f, _, _, c) => Deck.InsertColumn(f, c, true));
    void OnDeleteRow(object s, RoutedEventArgs e) => TableOp((f, _, r, _) => { if (!Deck.DeleteRow(f, r)) f.Remove(); cell = null; });
    void OnDeleteCol(object s, RoutedEventArgs e) => TableOp((f, _, _, c) => { if (!Deck.DeleteColumn(f, c)) f.Remove(); cell = null; });
    void OnDeleteTable(object s, RoutedEventArgs e) => TableOp((f, _, _, _) => f.Remove());

    // ---------- format painter ----------

    /// <summary>The format the painter carries until the next click on a shape.</summary>
    Deck.Format? painter;
    /// <summary>Ctrl+Shift+C: the format Ctrl+Shift+V gives the selected shapes.</summary>
    Deck.Format? copiedFormat;

    internal void OnFormatPainter(object? sender, RoutedEventArgs? e)
    {
        CommitEdit();
        if (selected == null) { StatusText.Text = L.T("selectShapeFirst"); return; }
        painter = Deck.TakeFormat(selected);
        pendingShape = null;
        Stage.Cursor = Cursors.Hand;
        StatusText.Text = L.T("painterHint");
    }

    internal void PaintFormat(OpenXmlElement target)
    {
        if (Deck == null || painter is not { } f) return;
        EndPainter();
        Deck.Snapshot(CurrentSlide!);
        Deck.ApplyFormat(target, f);
        selected = target;
        Changed();
    }

    void EndPainter()
    {
        if (painter == null) return;
        painter = null;
        Stage.Cursor = null;
        UpdateStatus();
    }

    internal void CopyFormat()
    {
        if (selected == null) { StatusText.Text = L.T("selectShapeFirst"); return; }
        copiedFormat = Deck.TakeFormat(selected);
        StatusText.Text = L.T("formatCopied");
    }

    internal void PasteFormat()
    {
        if (copiedFormat is not { } f) return;
        ShapeOp((el, _) => Deck.ApplyFormat(el, f), el => Deck.CanFormatShape(el) || Deck.HasFormat(el));
    }
}
