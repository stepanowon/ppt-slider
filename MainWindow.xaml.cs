using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
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
    List<ShapeHit> hits = [];
    OpenXmlElement? selected;
    TextBox? editBox;
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
        foreach (var c in PenState.Palette)
        {
            var b = new Button { Width = 18, Height = 18, Margin = new Thickness(2), Background = new SolidColorBrush(c), Focusable = false, BorderBrush = Brushes.Gray, ToolTip = "펜/형광펜 색" };
            b.Click += (_, _) => Pen.SetColor(c);
            Swatches.Children.Add(b);
        }
        OnRefreshMonitors(null, null);
        Pen.Changed += OnPenChanged;
        OnPenChanged();
        UpdateStatus();
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
        if (!ink.TryGetValue(sp, out var s)) ink[sp] = s = new StrokeCollection();
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
        for (int i = 0; i < Count; i++) Thumbs.Items.Add(new ListBoxItem { Content = ThumbContent(i), Focusable = false });
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
            if (e.ClickCount == 2 && Deck.HasText(selected)) { BeginEdit(); return; }
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

    void BeginEdit()
    {
        if (SelectedHit is not { } h || !Deck.HasText(h.Element)) return;
        var size = double.TryParse(Deck.FirstRun(h.Element, "sz"), out var sz) ? sz / 100 : 18;
        editBox = new TextBox
        {
            Text = Deck.GetText(h.Element), Tag = h.Element,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontSize = size * 96 / 72, FontFamily = new FontFamily("Malgun Gothic"),
            Width = Math.Max(h.Bounds.Width, 120), MinHeight = h.Bounds.Height,
            BorderBrush = (Brush)FindResource("Accent"), BorderThickness = new Thickness(2 * Px),
        };
        Canvas.SetLeft(editBox, h.Bounds.X); Canvas.SetTop(editBox, h.Bounds.Y);
        editBox.LostKeyboardFocus += (_, _) => CommitEdit();
        Overlay.Children.Add(editBox);
        editBox.Focus();
        editBox.SelectAll();
        StatusText.Text = "텍스트 편집 중 — 바깥을 클릭하면 적용, Esc는 취소";
    }

    void CommitEdit(bool cancel = false)
    {
        if (editBox is not { } tb) return;
        editBox = null; // first: removing the box fires LostKeyboardFocus again
        Overlay.Children.Remove(tb);
        UpdateStatus();
        var el = (OpenXmlElement)tb.Tag;
        if (cancel || tb.Text == Deck.GetText(el)) return;
        Deck!.Snapshot(CurrentSlide!);
        Deck.SetText(el, tb.Text);
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
    void OnBold(object? s, RoutedEventArgs? e) => ToggleRun("b", "1", "0");
    void OnItalic(object? s, RoutedEventArgs? e) => ToggleRun("i", "1", "0");
    void OnUnderline(object? s, RoutedEventArgs? e) => ToggleRun("u", "sng", "none");
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

    void StartShow(int from)
    {
        if (Deck == null) return;
        CommitEdit();
        show?.Close();
        Go(from);
        show = new ShowWindow(this, SelectedMonitor);
        show.Closed += (_, _) => { show = null; Activate(); };
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

    void OnHelp(object sender, RoutedEventArgs e) => MessageBox.Show(this, """
        [이동]  → ↓ PageDown Space : 다음    ← ↑ PageUp Backspace : 이전    Home / End
        [판서]  P : 펜    H : 형광펜    E : 지우개    Esc : 선택(쇼에서는 한 번 더 누르면 종료)
                1~7 : 색 (검정, 빨강, 파랑, 초록, 노랑, 흰색, 주황)
                [ 또는 - : 가늘게    ] 또는 + : 굵게    C : 현재 슬라이드 판서 지우기
        [쇼]    F5 : 처음부터    Shift+F5 : 현재 슬라이드부터    T : Break Time 타이머
        [편집]  더블클릭/F2 : 텍스트 편집    Delete : 개체 삭제    드래그 : 이동, 오른쪽 아래 모서리 : 크기
                Ctrl+B/I/U : 굵게/기울임/밑줄    Ctrl+Shift+. / , : 글꼴 크기    Ctrl+Z : 실행 취소
                Ctrl+M : 새 슬라이드    Ctrl+D : 슬라이드 복제    Ctrl+O : 열기    Ctrl+S : 저장
        """, "단축키");

    // ---------- keyboard ----------

    /// <summary>Navigation and ink shortcuts shared by the editor and the slide show.</summary>
    public bool HandleCommonKey(Key k)
    {
        switch (k)
        {
            case Key.Right or Key.Down or Key.PageDown or Key.Space or Key.Enter or Key.N: Go(Current + 1); break;
            case Key.Left or Key.Up or Key.PageUp or Key.Back: Go(Current - 1); break;
            case Key.Home: Go(0); break;
            case Key.End: Go(Count - 1); break;
            case Key.P: Pen.SetTool(Tool.Pen); break;
            case Key.H: Pen.SetTool(Tool.Highlighter); break;
            case Key.E: Pen.SetTool(Tool.Eraser); break;
            case >= Key.D1 and <= Key.D7: Pen.SetColor(PenState.Palette[k - Key.D1]); break;
            case >= Key.NumPad1 and <= Key.NumPad7: Pen.SetColor(PenState.Palette[k - Key.NumPad1]); break;
            case Key.OemOpenBrackets or Key.OemMinus or Key.Subtract: Pen.Grow(-1); break;
            case Key.OemCloseBrackets or Key.OemPlus or Key.Add: Pen.Grow(1); break;
            case Key.C: ClearInk(); break;
            case Key.T: OpenBreak(); break;
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
            return;
        }
        if (Keyboard.FocusedElement is TextBox) return; // typing in the font / size boxes
        var k = RealKey(e);
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        e.Handled = true;
        if (ctrl)
        {
            switch (k)
            {
                case Key.O: OnOpen(null, null); break;
                case Key.S: Save(shift); break;
                case Key.Z: OnUndo(null, null); break;
                case Key.B: OnBold(null, null); break;
                case Key.I: OnItalic(null, null); break;
                case Key.U: OnUnderline(null, null); break;
                case Key.M: OnNewSlide(null, null); break;
                case Key.D: OnDuplicate(null, null); break;
                case Key.OemPeriod when shift: OnGrow(null, null); break;
                case Key.OemComma when shift: OnShrink(null, null); break;
                default: e.Handled = false; break;
            }
            return;
        }
        switch (k)
        {
            case Key.F5: StartShow(shift ? Current : 0); break;
            case Key.F2: BeginEdit(); break;
            case Key.Delete when selected != null: OnDeleteShape(null, null); break;
            case Key.Escape: Pen.SetTool(Tool.Select); selected = null; DrawSelection(); break;
            default: e.Handled = HandleCommonKey(k); break;
        }
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
