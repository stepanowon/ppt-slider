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

    /// <summary>Screen ink per slide (screen coordinates of the overlay, see <see cref="InkOverlay"/>).</summary>
    readonly Dictionary<SlidePart, StrokeCollection> ink = new();
    /// <summary>Screen ink while no file is open.</summary>
    readonly StrokeCollection desktopInk = new();
    readonly InkHistory inkLog = new();
    InkOverlay? overlay;
    bool inkHidden;
    List<ShapeHit> hits = [];
    OpenXmlElement? selected;
    RichTextBox? editBox;
    FrameworkElement? editHidden; // the drawn text the editor covers
    List<Deck.Fmt> editStart = []; // bold / italic / underline per character when editing began
    double editScale = 1; // the font scale the editor shows the text at
    ShowWindow? show;
    bool syncing;
    // dragging a selected shape
    Point dragStart;
    Rect dragOrig, dragRect;
    bool dragging, resizing;

    public MainWindow()
    {
        InitializeComponent();
        var lang = XmlLanguage.GetLanguage(L.Lang switch { "ko" => "ko-kr", "ja" => "ja-jp", _ => "en-us" });
        FontBox.ItemsSource = Fonts.SystemFontFamilies
            .Select(f => f.FamilyNames.TryGetValue(lang, out var n) ? n : f.Source).Distinct().OrderBy(n => n).ToList();
        SizeBox.ItemsSource = FontSizes;
        foreach (var (c, i) in PenState.Palette.Select((c, i) => (c, i)))
        {
            var b = new Button { Width = 18, Height = 18, Margin = new Thickness(2), Background = new SolidColorBrush(c), Focusable = false, BorderBrush = Brushes.Gray, ToolTip = L.T("penColor") };
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
        inkLog.Track(desktopInk);
        desktopInk.StrokesChanged += (_, _) => UpdateOverlay();
        LocationChanged += (_, _) => { if (show == null && overlay?.IsVisible == true) UpdateOverlay(); };
        AskRecover = path => MessageBox.Show(this,
            L.F("recover", System.IO.Path.GetFileName(path)), L.T("recoverTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        FillLanguages();
        ConfirmDeleteSlide = n => MessageBox.Show(this, L.F("deleteSlideAsk", n), L.T("deleteSlide"),
            MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
        autoSaveTimer.Tick += (_, _) => AutoSaveNow();
        autoSaveTimer.Start();
        UpdateRecent();
    }

    // ---------- document ----------

    /// <summary>Asked when an autosaved copy newer than the file exists: true = open the copy. The self-test answers it.</summary>
    internal Func<string, bool> AskRecover;

    /// <summary>Asked before deleting slide n (1-based): true = delete. The self-test answers it.</summary>
    internal Func<int, bool> ConfirmDeleteSlide;

    public void Open(string path)
    {
        if (!ConfirmDiscard()) return;
        if (Deck != null) AutoSave.Delete(Deck.Path); // just saved, or the user let the edits go
        Deck d;
        try
        {
            d = AutoSave.Recoverable(path) is { } copy && AskRecover(path) ? Deck.Recover(path, copy) : Deck.Open(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.F("openFail", ex.Message), L.T("open"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        show?.Close();
        if (!d.Dirty) AutoSave.Delete(path); // no copy, or it was declined
        Deck?.Dispose();
        Deck = d;
        RecentFiles.Add(path);
        UpdateRecent();
        ink.Clear();
        inkLog.Clear();
        inkLog.Track(desktopInk);
        selected = null;
        StageFrame.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        Stage.Width = d.Renderer.Width;
        Stage.Height = d.Renderer.Height;
        RebuildThumbs();
        Current = -1;
        Go(0);
        if (d.Dirty) StatusText.Text = L.T("recovered");
        UpdateTitle();
    }

    // ---------- recent files, autosave ----------

    readonly DispatcherTimer autoSaveTimer = new() { Interval = TimeSpan.FromMinutes(2) };

    /// <summary>Copies the deck aside when it has unsaved edits (every 2 minutes).</summary>
    internal void AutoSaveNow()
    {
        if (Deck is not { Dirty: true } d) return;
        try { AutoSave.Save(d); }
        catch (Exception) { } // best effort; the next tick tries again
    }

    void UpdateRecent()
    {
        var files = RecentFiles.Load().Where(File.Exists).ToList();
        RecentBtn.IsEnabled = files.Count > 0;
        RecentTitle.Visibility = files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentList.ItemsSource = files.Select(f =>
        {
            var b = new Button
            {
                Content = System.IO.Path.GetFileName(f), ToolTip = f, Tag = f, Focusable = false, Cursor = Cursors.Hand,
                Foreground = (Brush)FindResource("Accent"), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 3, 0, 3), FontSize = 14,
            };
            b.Click += (_, _) => Open(f);
            return b;
        }).ToList();
    }

    void OnRecentMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = RecentBtn, Placement = PlacementMode.Bottom };
        foreach (var f in RecentFiles.Load().Where(File.Exists))
        {
            var item = new MenuItem { Header = System.IO.Path.GetFileName(f), ToolTip = f };
            item.Click += (_, _) => Open(f);
            menu.Items.Add(item);
        }
        menu.IsOpen = menu.Items.Count > 0;
    }

    /// <summary>Saves the slides; screen ink is not part of the file (see <see cref="OnInkNotes"/> to keep it as a PDF).</summary>
    bool Save(bool saveAs)
    {
        if (Deck == null) return true;
        CommitEdit();
        var path = Deck.Path;
        if (saveAs)
        {
            var dlg = new SaveFileDialog
            {
                Filter = L.T("pptxFilter"),
                InitialDirectory = System.IO.Path.GetDirectoryName(Deck.Path),
                FileName = System.IO.Path.GetFileName(Deck.Path),
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        try
        {
            var before = Deck.Path;
            Deck.Save(path);
            AutoSave.Delete(before); // saved over (or away from) it: the copy is stale
            StatusText.Text = L.F("saved", path);
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.F("saveFail", ex.Message), L.T("save"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Every slide as a PDF page; with ink drawn on slides the user picks a version with or without it.</summary>
    void OnExportPdf(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        CommitEdit();
        bool withInk = false;
        var slideInk = SlideInk();
        if (slideInk.Count > 0)
        {
            var r = MessageBox.Show(this, L.T("pdfWithInk"), L.T("exportPdf"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            withInk = r == MessageBoxResult.Yes;
        }
        ExportPdf(withInk ? slideInk : null, withInk ? L.T("inkSuffix") : "", onlyInked: false);
    }

    /// <summary>Lecture notes: only the slides that were drawn on, each with its ink.</summary>
    void OnInkNotes(object? sender, RoutedEventArgs? e)
    {
        if (Deck == null) return;
        CommitEdit();
        var slideInk = SlideInk();
        if (slideInk.Count == 0) { StatusText.Text = L.T("noInkedSlides"); return; }
        ExportPdf(slideInk, L.T("inkNotesSuffix"), onlyInked: true);
    }

    void ExportPdf(IReadOnlyDictionary<SlidePart, StrokeCollection>? slideInk, string suffix, bool onlyInked)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(Deck!.Path);
        var dlg = new SaveFileDialog
        {
            Filter = L.T("pdfFilter"),
            InitialDirectory = System.IO.Path.GetDirectoryName(Deck.Path),
            FileName = name + suffix + ".pdf",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Cursor = Cursors.Wait;
            PdfExport.Export(Deck, dlg.FileName, slideInk, onlyInked);
            StatusText.Text = L.F("pdfDone", dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.F("pdfFail", ex.Message), L.T("exportPdf"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Cursor = null; }
    }

    bool ConfirmDiscard()
    {
        if (Deck == null || !Deck.Dirty) return true;
        var r = MessageBox.Show(this, L.T("unsaved"), "PPT Slider",
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
        UpdateStatus();
        SlideChanged?.Invoke();
        UpdateOverlay(); // the new slide's own ink
    }

    public StrokeCollection StrokesOf(SlidePart sp)
    {
        return ink.TryGetValue(sp, out var s) ? s : NewInk(sp, new StrokeCollection());
    }

    StrokeCollection NewInk(SlidePart sp, StrokeCollection s)
    {
        ink[sp] = s;
        inkLog.Track(s);
        s.StrokesChanged += (_, _) => UpdateOverlay();
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

    void UpdateTitle() =>
        Title = Deck == null ? "PPT Slider" : $"{System.IO.Path.GetFileName(Deck.Path)}{(Deck.Dirty ? " *" : "")} - PPT Slider";

    void UpdateStatus() =>
        StatusText.Text = Deck == null ? L.T("openAFile") : L.F("slideOf", Current + 1, Count);

    void OnThumbSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && Thumbs.SelectedIndex >= 0) Go(Thumbs.SelectedIndex);
    }

    /// <summary>Self-test: drops the document without asking to save, then closes.</summary>
    internal void CloseForTest()
    {
        show?.Close();
        ink.Clear();
        desktopInk.Clear();
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
            case "blankPage": OnBlankPage(null, null); break;
            case "exportPdf": OnExportPdf(null, null); break;
            case "inkNotes": OnInkNotes(null, null); break;
            case "picture": OnAddPicture(null, null); break;
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
            else if (Shortcuts.Find(Gesture.From(e), inShow: false) is { } id)
            {
                if (id is "bold" or "italic" or "underline") { ToggleInEditor(id); e.Handled = true; }
                else if (id is "grow" or "shrink") e.Handled = Run(id); // on the selected characters
            }
            return;
        }
        if (Keyboard.FocusedElement is TextBox) return; // typing in the font / size boxes
        var g = Gesture.From(e);
        if (g is { Key: Key.V, Mods: ModifierKeys.Control } && PastePicture()) { e.Handled = true; return; }
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
        var dlg = new OpenFileDialog { Filter = L.T("pptxFilter") };
        if (dlg.ShowDialog(this) == true) Open(dlg.FileName);
    }

    void OnSave(object sender, RoutedEventArgs e) => Save(false);
    void OnSaveAs(object sender, RoutedEventArgs e) => Save(true);

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        if (files.FirstOrDefault(f => f.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)) is { } f) Open(f);
        else if (Deck != null && files.FirstOrDefault(p => Deck.PictureType(System.IO.Path.GetExtension(p)) != null) is { } pic) InsertPictureFile(pic);
    }

    bool closing;

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!restarting && !ConfirmDiscard()) { e.Cancel = true; return; }
        closing = true;
        show?.Close();
        overlay?.Close();
        if (Deck != null) AutoSave.Delete(Deck.Path); // a normal close: no copy left to recover
        autoSaveTimer.Stop();
        Deck?.Dispose();
    }
}
