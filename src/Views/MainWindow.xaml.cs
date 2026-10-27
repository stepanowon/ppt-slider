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

public partial class MainWindow : Window
{
    static readonly double[] FontSizes = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 44, 48, 54, 60, 66, 72, 80, 88, 96];
    static readonly Color[] TextColors =
    [
        Colors.Black, Colors.White, Color.FromRgb(0x44, 0x54, 0x6A), Color.FromRgb(0x44, 0x72, 0xC4), Color.FromRgb(0xED, 0x7D, 0x31),
        Color.FromRgb(0xA5, 0xA5, 0xA5), Color.FromRgb(0xFF, 0xC0, 0x00), Color.FromRgb(0x5B, 0x9B, 0xD5), Color.FromRgb(0x70, 0xAD, 0x47),
        Color.FromRgb(0xC0, 0x00, 0x00), Colors.Red, Color.FromRgb(0x00, 0x20, 0x60), Color.FromRgb(0x70, 0x30, 0xA0),
    ];

    public Deck? Deck { get; private set; }
    public int Current { get; private set; }
    public int Count => Deck?.Slides.Count ?? 0;
    public SlidePart? CurrentSlide => Deck?.Slides.ElementAtOrDefault(Current);
    public PenState Pen { get; } = new();
    /// <summary>Raised when the current slide changes or its content is edited.</summary>
    public event Action? SlideChanged;

    readonly Dictionary<SlidePart, StrokeCollection> ink = new();
    readonly InkHistory inkLog = new();
    List<ShapeHit> hits = [];
    OpenXmlElement? selected;
    RichTextBox? editBox;
    FrameworkElement? editHidden; // the drawn text the editor covers
    List<Deck.Fmt> editStart = []; // bold / italic / underline per character when editing began
    ShowWindow? show;
    bool syncing;
    // dragging a selected shape
    Point dragStart;
    Rect dragOrig, dragRect;
    bool dragging, resizing;

    public MainWindow()
    {
        InitializeComponent();
        var ko = XmlLanguage.GetLanguage("ko-kr");
        FontBox.ItemsSource = Fonts.SystemFontFamilies
            .Select(f => f.FamilyNames.TryGetValue(ko, out var n) ? n : f.Source).Distinct().OrderBy(n => n).ToList();
        SizeBox.ItemsSource = FontSizes;
        foreach (var (c, i) in PenState.Palette.Select((c, i) => (c, i)))
        {
            var b = new Button { Width = 18, Height = 18, Margin = new Thickness(2), Background = new SolidColorBrush(c), Focusable = false, BorderBrush = Brushes.Gray, ToolTip = "펜/형광펜 색" };
            Shortcuts.SetFor(b, $"color{i + 1}");
            b.Click += (_, _) => Pen.SetColor(c);
            Swatches.Children.Add(b);
        }
        OnRefreshMonitors(null, null);
        Pen.Changed += OnPenChanged;
        OnPenChanged();
        UpdateStatus();
        UpdateHint();
        Shortcuts.Changed += UpdateHint;
        jumpTimer.Tick += OnJumpTimeout;
    }

    // ---------- document ----------

    public void Open(string path)
    {
        if (!ConfirmDiscard()) return;
        Deck d;
        try { d = Deck.Open(path); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"파일을 열 수 없습니다.\n{ex.Message}\n\n.pptx 형식만 지원합니다.", "열기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        show?.Close();
        Deck?.Dispose();
        Deck = d;
        ink.Clear();
        inkLog.Clear();
        selected = null;
        StageFrame.Visibility = Visibility.Visible;
        EmptyHint.Visibility = Visibility.Collapsed;
        Stage.Width = d.Renderer.Width;
        Stage.Height = d.Renderer.Height;
        RebuildThumbs();
        Current = -1;
        Go(0);
    }

    /// <summary>Saves; when ink exists the user picks a version with or without it.</summary>
    bool Save(bool saveAs)
    {
        if (Deck == null) return true;
        CommitEdit();
        bool withInk = false;
        if (ink.Values.Any(s => s.Count > 0))
        {
            var r = MessageBox.Show(this,
                "판서한 내용이 있습니다. 어떤 버전으로 저장할까요?\n\n[예]  판서를 포함하여 저장 (새 파일)\n[아니요]  판서 없이 저장\n[취소]  저장하지 않음",
                "저장", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            withInk = r == MessageBoxResult.Yes;
        }
        var path = Deck.Path;
        if (saveAs || withInk)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(Deck.Path);
            var dlg = new SaveFileDialog
            {
                Filter = "PowerPoint 프레젠테이션 (*.pptx)|*.pptx",
                InitialDirectory = System.IO.Path.GetDirectoryName(Deck.Path),
                FileName = withInk ? name + "_판서.pptx" : name + ".pptx",
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        try
        {
            Deck.Save(path, withInk ? ink : null);
            StatusText.Text = $"저장됨: {path}";
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"저장하지 못했습니다.\n{ex.Message}", "저장", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    bool ConfirmDiscard()
    {
        if (Deck == null || (!Deck.Dirty && !ink.Values.Any(s => s.Count > 0))) return true;
        var r = MessageBox.Show(this, "저장하지 않은 변경 내용(또는 판서)이 있습니다. 저장할까요?", "PPT Slider",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(false));
    }

    // ---------- navigation & rendering ----------

    public void Go(int i)
    {
        if (Deck == null || Count == 0) return;
        i = Math.Clamp(i, 0, Count - 1);
        CommitEdit();
        if (i != Current) selected = null;
        Current = i;
        RenderCurrent();
        syncing = true;
        Thumbs.SelectedIndex = i;
        Thumbs.ScrollIntoView(Thumbs.SelectedItem);
        syncing = false;
        Ink.Strokes = StrokesOf(CurrentSlide!);
        UpdateStatus();
        SlideChanged?.Invoke();
    }

    public StrokeCollection StrokesOf(SlidePart sp)
    {
        if (!ink.TryGetValue(sp, out var s))
        {
            ink[sp] = s = new StrokeCollection();
            inkLog.Track(s);
        }
        return s;
    }

    void RenderCurrent()
    {
        hits = [];
        SlideHost.Content = Deck!.Renderer.Render(CurrentSlide!, hits);
        if (selected != null && hits.All(h => h.Element != selected)) selected = null;
        DrawSelection();
    }

    /// <summary>After an edit: redraw, refresh the thumbnail, tell the show window.</summary>
    void Changed()
    {
        RenderCurrent();
        if (Thumbs.Items[Current] is ListBoxItem item) item.Content = ThumbContent(Current);
        UpdateTitle();
        SyncFormatUi();
        SlideChanged?.Invoke();
    }

    void RebuildThumbs()
    {
        syncing = true;
        Thumbs.Items.Clear();
        for (int i = 0; i < Count; i++)
        {
            var item = new ListBoxItem { Content = ThumbContent(i), Focusable = false };
            // a ListBoxItem only selects itself on click by taking focus, which a non-focusable one can't: handle the click here
            item.PreviewMouseLeftButtonDown += (_, e) => { Go(Thumbs.Items.IndexOf(item)); e.Handled = true; };
            Thumbs.Items.Add(item);
        }
        syncing = false;
        UpdateTitle();
    }

    FrameworkElement ThumbContent(int i)
    {
        var num = new TextBlock { Text = (i + 1).ToString(), Width = 22, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Top };
        var view = new Border
        {
            BorderBrush = Brushes.Silver, BorderThickness = new Thickness(1), Background = Brushes.White,
            Child = new Viewbox { Width = 168, Child = Deck!.Renderer.Render(Deck.Slides[i]) },
        };
        var dp = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(num, Dock.Left);
        dp.Children.Add(num);
        dp.Children.Add(view);
        return dp;
    }

    void UpdateTitle() =>
        Title = Deck == null ? "PPT Slider" : $"{System.IO.Path.GetFileName(Deck.Path)}{(Deck.Dirty ? " *" : "")} - PPT Slider";

    void UpdateStatus() =>
        StatusText.Text = Deck == null ? "파일을 열어 주세요" : $"슬라이드 {Current + 1} / {Count}";

    void OnThumbSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && Thumbs.SelectedIndex >= 0) Go(Thumbs.SelectedIndex);
    }

    // ---------- selection, move, resize, in-place text edit ----------

    ShapeHit? SelectedHit => hits.FirstOrDefault(h => h.Element == selected);

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
            pad = panel.Margin;
            origin = panel.TranslatePoint(new Point(-pad.Left, -pad.Top), Stage);
            origin.X = h.Bounds.X; // the panel stretches to the shape's width
            editHidden = panel;
            panel.Visibility = Visibility.Hidden;
        }
        else
        {
            doc = new FlowDocument { PagePadding = new Thickness(0) };
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
        box.LostKeyboardFocus += (_, _) => CommitEdit();
        Overlay.Children.Add(box);
        box.Focus();
        if (caretAt is { } pt)
            Dispatcher.BeginInvoke(() =>
            {
                if (editBox == box && box.GetPositionFromPoint(Stage.TranslatePoint(pt, box), true) is { } pos) box.CaretPosition = pos;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        else box.SelectAll();
        StatusText.Text = "텍스트 편집 중 — 바깥을 클릭하면 적용, Esc는 취소, Shift+Enter는 줄 바꿈";
    }

    /// <summary>The editor's text in <see cref="Deck.GetEditText"/> form, with each character's bold / italic / underline.</summary>
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
                        var f = new Deck.Fmt(r.FontWeight >= FontWeights.Bold, r.FontStyle == FontStyles.Italic, u);
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
        var el = (OpenXmlElement)box.Tag;
        var (text, fmt) = EditText(box.Document);
        if (cancel || (text == Deck.GetEditText(el) && fmt.SequenceEqual(editStart))) return;
        Deck!.Snapshot(CurrentSlide!);
        Deck.SetEditText(el, text, fmt, editStart);
        Changed();
    }

    // ---------- formatting ----------

    /// <summary>Runs an edit on the selected text shape with undo + redraw.</summary>
    void EditSelected(Action<OpenXmlElement> f)
    {
        CommitEdit();
        if (Deck == null || selected == null || !Deck.HasText(selected))
        {
            StatusText.Text = "먼저 슬라이드에서 텍스트 개체를 선택하세요";
            return;
        }
        Deck.Snapshot(CurrentSlide!);
        f(selected);
        Changed();
    }

    void SetFontSize(double pt) => EditSelected(s => Deck.EditRuns(s, r => Deck.SetAttr(r, "sz", ((int)Math.Round(pt * 100)).ToString())));

    double SelectedSize => selected != null && double.TryParse(Deck.FirstRun(selected, "sz"), out var v) ? v / 100 : 18;

    void ToggleRun(string attr, string on, string off)
    {
        if (selected == null) { EditSelected(_ => { }); return; }
        var value = Deck.FirstRun(selected, attr) is { } v && v != off && v != "0" ? off : on;
        EditSelected(s => Deck.EditRuns(s, r => Deck.SetAttr(r, attr, value)));
    }

    void SyncFormatUi()
    {
        syncing = true;
        SizeBox.Text = selected != null && Deck.HasText(selected) ? SelectedSize.ToString() : "";
        FontBox.Text = selected == null ? "" : Deck.FirstRunFont(selected) ?? "";
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
        if (!syncing && FontBox.SelectedItem is string face) EditSelected(s => Deck.EditRuns(s, r => Deck.SetFont(r, face)));
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
        selected = Deck.AddTextBox(CurrentSlide!, new Rect(w * 0.3, h * 0.42, w * 0.4, h * 0.12), "텍스트를 입력하세요");
        Changed();
        BeginEdit();
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
        RebuildThumbs();
        Go(Current + 1);
    }

    void OnDuplicate(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        Deck.Duplicate(Current);
        RebuildThumbs();
        Go(Current + 1);
    }

    void OnDeleteSlide(object sender, RoutedEventArgs e)
    {
        if (Deck == null) return;
        if (Count <= 1) { StatusText.Text = "마지막 슬라이드는 삭제할 수 없습니다"; return; }
        if (MessageBox.Show(this, $"슬라이드 {Current + 1}을(를) 삭제할까요? 실행 취소할 수 없습니다.", "슬라이드 삭제",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        ink.Remove(CurrentSlide!);
        Deck.DeleteSlide(Current);
        RebuildThumbs();
        int i = Math.Min(Current, Count - 1);
        Current = -1;
        Go(i);
    }

    void OnUndo(object? sender, RoutedEventArgs? e)
    {
        CommitEdit();
        // ink first: Ctrl+Z right after drawing takes back the stroke, not an earlier edit
        if (CurrentSlide is { } cur && inkLog.Undo(StrokesOf(cur))) { StatusText.Text = "판서를 되돌렸습니다"; return; }
        if (Deck?.Undo() is not { } part) { StatusText.Text = "더 이상 실행 취소할 내용이 없습니다"; return; }
        selected = null;
        int i = Deck.Slides.IndexOf(part);
        if (Thumbs.Items[i] is ListBoxItem item) item.Content = ThumbContent(i);
        Current = -1;
        Go(i);
        UpdateTitle();
    }

    // ---------- ink ----------

    void OnPenChanged()
    {
        Pen.Apply(Ink);
        if (Pen.Tool != Tool.Select) { CommitEdit(); selected = null; DrawSelection(); }
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
            Tool.Pen => $"펜 · 두께 {Pen.PenSize:0}",
            Tool.Highlighter => $"형광펜 · 두께 {Pen.HiSize:0}",
            Tool.Eraser => "지우개",
            Tool.Laser => "레이저 포인터",
            _ => "선택",
        };
    }

    void OnTool(object sender, RoutedEventArgs e) =>
        Pen.SetTool(sender == ToolPen ? Tool.Pen : sender == ToolHi ? Tool.Highlighter : sender == ToolEraser ? Tool.Eraser : Tool.Select);

    void OnPenSize(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!syncing) Pen.SetSize(Math.Round(e.NewValue));
    }

    public void ClearInk() { if (CurrentSlide is { } s) StrokesOf(s).Clear(); }

    void OnClearInk(object sender, RoutedEventArgs e) => ClearInk();

    void OnClearAllInk(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "모든 슬라이드의 판서를 지울까요?", "판서 지우기", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            foreach (var s in ink.Values) s.Clear();
    }

    // ---------- slide show, monitors, break timer ----------

    void OnRefreshMonitors(object? sender, RoutedEventArgs? e)
    {
        var all = Monitors.All();
        MonitorBox.ItemsSource = all;
        MonitorBox.SelectedItem = all.LastOrDefault(m => !m.Primary) ?? all.FirstOrDefault(); // projector by default
    }

    MonitorInfo SelectedMonitor => MonitorBox.SelectedItem as MonitorInfo ?? Monitors.All()[0];

    internal ShowWindow? ShowView => show;

    /// <param name="test">Self-test: a small window off screen instead of full screen.</param>
    internal void StartShow(int from, bool test = false)
    {
        if (Deck == null) return;
        CommitEdit();
        show?.Close();
        Go(from);
        ShowStart = DateTime.Now;
        show = new ShowWindow(this, SelectedMonitor, test);
        show.Closed += (_, _) =>
        {
            show = null;
            SetJump("");
            SetBlank(Blank.None);
            if (Pen.Tool == Tool.Laser) Pen.SetTool(Tool.Select);
            Activate();
        };
        show.Show();
    }

    void OnShowFirst(object sender, RoutedEventArgs e) => StartShow(0);
    void OnShowCurrent(object sender, RoutedEventArgs e) => StartShow(Current);

    public void OpenBreak()
    {
        var w = new BreakWindow(show?.Monitor ?? SelectedMonitor) { Owner = show ?? (Window)this };
        w.Show();
    }

    void OnBreak(object sender, RoutedEventArgs e) => OpenBreak();

    void OnHelp(object? sender, RoutedEventArgs? e)
    {
        var lines = Shortcuts.All.GroupBy(a => a.Group).Select(g =>
            $"[{g.Key}]\n" + string.Join("\n", g.Where(a => Shortcuts.Of(a.Id).Count > 0).Select(a => $"    {a.Name} : {Shortcuts.Text(a.Id)}")));
        MessageBox.Show(this, string.Join("\n", lines) + """

            [고정]
                Esc : 텍스트 편집 취소 / 선택 도구로 (쇼에서는 한 번 더 누르면 종료)
                쇼에서 숫자 + Enter : 해당 번호 슬라이드로 이동 (숫자만 누르면 1초 뒤 펜 색)
                더블클릭 : 텍스트 편집    드래그 : 이동, 오른쪽 아래 모서리 : 크기

            단축키는 [슬라이드 쇼] 탭의 [단축키 설정]에서 바꿀 수 있습니다.
            """, "단축키");
    }

    void OnShortcutSettings(object? sender, RoutedEventArgs? e) => new ShortcutsWindow { Owner = this }.ShowDialog();

    void UpdateHint() =>
        EmptyHint.Text = "PPTX 파일을 열거나 이곳으로 끌어다 놓으세요" + (Shortcuts.Text("open") is { Length: > 0 } k ? $" ({k})" : "");

    // ---------- slide show keys and state (shared by the show window and the presenter view) ----------

    public Blank Blanked { get; private set; }
    /// <summary>Ink drawn on the black / white screen; a new blank screen starts empty.</summary>
    public StrokeCollection BlankInk { get; private set; } = new();
    /// <summary>The slide number being typed in the show ("" when none).</summary>
    public string Jump { get; private set; } = "";
    public DateTime ShowStart { get; private set; }
    /// <summary>Raised when the blank screen or the typed slide number changes.</summary>
    public event Action? ShowStateChanged;
    readonly DispatcherTimer jumpTimer = new();
    Gesture? jumpKey; // a lone digit: runs its own command (a pen color) if no Enter / second digit follows

    /// <summary>The strokes the show draws on: the blank screen's while blanked, otherwise the slide's.</summary>
    public StrokeCollection ShowInk => Blanked != Blank.None ? BlankInk : StrokesOf(CurrentSlide!);

    public void SetBlank(Blank b)
    {
        if (b == Blanked) return;
        Blanked = b;
        BlankInk = new StrokeCollection();
        inkLog.Track(BlankInk);
        ShowStateChanged?.Invoke();
    }

    static string? Digit(Gesture g) => g.Mods != ModifierKeys.None ? null : g.Key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(g.Key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)(g.Key - Key.NumPad0)).ToString(),
        _ => null,
    };

    /// <summary>
    /// A key in the slide show. Digits type a slide number and Enter goes there; a lone digit with nothing after it
    /// for a second runs its own command instead (a pen color by default). Returns false when the key did nothing.
    /// </summary>
    public bool ShowKey(Gesture g)
    {
        if (Digit(g) is { } d)
        {
            jumpKey = Jump == "" ? g : null;
            SetJump(Jump.Length < 4 ? Jump + d : Jump);
            return true;
        }
        if (Jump != "")
        {
            var typed = Jump;
            switch (g)
            {
                case { Key: Key.Enter, Mods: ModifierKeys.None }:
                    SetJump("");
                    SetBlank(Blank.None);
                    if (int.TryParse(typed, out var n) && n >= 1) Go(n - 1);
                    return true;
                case { Key: Key.Back, Mods: ModifierKeys.None }:
                    jumpKey = null;
                    SetJump(typed[..^1]);
                    return true;
                case { Key: Key.Escape, Mods: ModifierKeys.None }:
                    SetJump("");
                    return true;
            }
            SetJump(""); // any other key drops the number and does its own thing
        }
        switch (Shortcuts.Find(g, inShow: true))
        {
            case null: return false;
            case "black": SetBlank(Blanked == Blank.Black ? Blank.None : Blank.Black); return true;
            case "white": SetBlank(Blanked == Blank.White ? Blank.None : Blank.White); return true;
            case "next" or "prev" or "first" or "last" when Blanked != Blank.None: SetBlank(Blank.None); return true; // like PowerPoint: back to the same slide
            case "laser": Pen.SetTool(Pen.Tool == Tool.Laser ? Tool.Select : Tool.Laser); return true;
            case "undo": inkLog.Undo(ShowInk); return true;
            case "clearInk": ShowInk.Clear(); return true;
            case { } id: return Run(id);
        }
    }

    void SetJump(string s)
    {
        Jump = s;
        jumpTimer.Stop();
        jumpTimer.Interval = TimeSpan.FromSeconds(jumpKey != null ? 1 : 4);
        if (s != "") jumpTimer.Start();
        ShowStateChanged?.Invoke();
    }

    void OnJumpTimeout(object? sender, EventArgs e)
    {
        var lone = jumpKey;
        jumpKey = null;
        SetJump("");
        if (lone is { } k && Shortcuts.Find(k, inShow: true) is { } id) Run(id);
    }

    /// <summary>Self-test: drops the document without asking to save, then closes.</summary>
    internal void CloseForTest()
    {
        show?.Close();
        ink.Clear();
        Deck?.Dispose();
        Deck = null;
        Close();
    }

    // ---------- keyboard ----------

    /// <summary>
    /// Runs a remappable command (see <see cref="Shortcuts.All"/>); the slide show only looks up InShow ones.
    /// Returns false when the command did not apply.
    /// </summary>
    public bool Run(string id)
    {
        if (id.StartsWith("color") && int.TryParse(id[5..], out var n) && n >= 1 && n <= PenState.Palette.Length)
        {
            Pen.SetColor(PenState.Palette[n - 1]);
            return true;
        }
        switch (id)
        {
            case "next": Go(Current + 1); break;
            case "prev": Go(Current - 1); break;
            case "first": Go(0); break;
            case "last": Go(Count - 1); break;
            case "select": Pen.SetTool(Tool.Select); break;
            case "pen": Pen.SetTool(Tool.Pen); break;
            case "highlighter": Pen.SetTool(Tool.Highlighter); break;
            case "eraser": Pen.SetTool(Tool.Eraser); break;
            case "thinner": Pen.Grow(-1); break;
            case "thicker": Pen.Grow(1); break;
            case "clearInk": ClearInk(); break;
            case "break": OpenBreak(); break;
            case "showFirst": StartShow(0); break;
            case "showCurrent": StartShow(Current); break;
            case "open": OnOpen(null, null); break;
            case "save": Save(false); break;
            case "saveAs": Save(true); break;
            case "undo": OnUndo(null, null); break;
            case "editText": BeginEdit(); break;
            case "deleteShape" when selected != null: OnDeleteShape(null, null); break;
            case "bold": OnBold(null, null); break;
            case "italic": OnItalic(null, null); break;
            case "underline": OnUnderline(null, null); break;
            case "grow": OnGrow(null, null); break;
            case "shrink": OnShrink(null, null); break;
            case "newSlide": OnNewSlide(null, null); break;
            case "duplicate": OnDuplicate(null, null); break;
            case "help": OnHelp(null, null); break;
            case "settings": OnShortcutSettings(null, null); break;
            default: return false;
        }
        return true;
    }

    /// <summary>With the Korean IME on, letter keys arrive as ImeProcessed.</summary>
    public static Key RealKey(KeyEventArgs e) => e.Key switch { Key.ImeProcessed => e.ImeProcessedKey, Key.System => e.SystemKey, _ => e.Key };

    void OnKey(object sender, KeyEventArgs e)
    {
        if (editBox != null)
        {
            if (e.Key == Key.Escape) { CommitEdit(cancel: true); e.Handled = true; }
            else if (Shortcuts.Find(Gesture.From(e), inShow: false) is { } id && id is "bold" or "italic" or "underline") { ToggleInEditor(id); e.Handled = true; }
            return;
        }
        if (Keyboard.FocusedElement is TextBox) return; // typing in the font / size boxes
        var g = Gesture.From(e);
        if (g is { Key: Key.Escape, Mods: ModifierKeys.None })
        {
            Pen.SetTool(Tool.Select); selected = null; DrawSelection();
            e.Handled = true;
        }
        else if (Shortcuts.Find(g, inShow: false) is { } id) e.Handled = Run(id);
    }

    // ---------- window ----------

    void OnOpen(object? sender, RoutedEventArgs? e)
    {
        var dlg = new OpenFileDialog { Filter = "PowerPoint 프레젠테이션 (*.pptx)|*.pptx" };
        if (dlg.ShowDialog(this) == true) Open(dlg.FileName);
    }

    void OnSave(object sender, RoutedEventArgs e) => Save(false);
    void OnSaveAs(object sender, RoutedEventArgs e) => Save(true);

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.FirstOrDefault(f => f.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)) is { } f)
            Open(f);
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        show?.Close();
        Deck?.Dispose();
    }
}
