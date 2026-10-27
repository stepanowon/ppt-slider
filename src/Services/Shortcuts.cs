using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace PptSlider;

/// <summary>A key plus modifiers. Stored as "Ctrl+Shift+OemPeriod" (enum names), shown as "Ctrl+Shift+.".</summary>
public readonly record struct Gesture(Key Key, ModifierKeys Mods)
{
    public static Gesture From(KeyEventArgs e) => new(MainWindow.RealKey(e), Keyboard.Modifiers);

    public static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.ImeProcessed or Key.System or Key.None;

    string Prefix(Func<Key, string> name) =>
        (Mods.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (Mods.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") +
        (Mods.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (Mods.HasFlag(ModifierKeys.Windows) ? "Win+" : "") + name(Key);

    public string Serialize() => Prefix(k => k.ToString());
    public override string ToString() => Prefix(Display);

    public static Gesture? Parse(string s)
    {
        var parts = s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Enum.TryParse<Key>(parts[^1], out var key) || IsModifierKey(key)) return null;
        var mods = ModifierKeys.None;
        foreach (var p in parts[..^1])
            mods |= p switch { "Ctrl" => ModifierKeys.Control, "Shift" => ModifierKeys.Shift, "Alt" => ModifierKeys.Alt, "Win" => ModifierKeys.Windows, _ => (ModifierKeys)(-1) };
        return (int)mods < 0 ? null : new Gesture(key, mods);
    }

    static string Display(Key k) => k switch
    {
        Key.Right => "→", Key.Left => "←", Key.Up => "↑", Key.Down => "↓",
        Key.PageUp => "PageUp", Key.PageDown => "PageDown", Key.Back => "Backspace", Key.Enter => "Enter", Key.Escape => "Esc",
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {k - Key.NumPad0}",
        Key.Add => "Num +", Key.Subtract => "Num -", Key.Multiply => "Num *", Key.Divide => "Num /", Key.Decimal => "Num .",
        Key.OemPlus => "=", Key.OemMinus => "-", Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]",
        Key.OemPeriod => ".", Key.OemComma => ",", Key.OemQuestion => "/", Key.Oem1 => ";", Key.OemQuotes => "'",
        Key.Oem3 => "`", Key.Oem5 => "\\",
        _ => k.ToString(),
    };
}

/// <summary>A remappable command. <see cref="InShow"/> commands also work in the slide show window.</summary>
public record ShortcutAction(string Id, string Group, string Name, bool InShow, Gesture[] Defaults);

/// <summary>
/// User-configurable key bindings, saved to %APPDATA%\PptSlider\shortcuts.json.
/// Esc is reserved (cancel edit / pointer / end show) and cannot be assigned.
/// </summary>
public static class Shortcuts
{
    static Gesture K(Key k, ModifierKeys m = ModifierKeys.None) => new(k, m);
    const ModifierKeys Ctrl = ModifierKeys.Control, CtrlShift = ModifierKeys.Control | ModifierKeys.Shift;

    public static readonly ShortcutAction[] All =
    [
        new("next", "이동", "다음 슬라이드", true, [K(Key.Right), K(Key.Down), K(Key.PageDown), K(Key.Space), K(Key.Enter), K(Key.N)]),
        new("prev", "이동", "이전 슬라이드", true, [K(Key.Left), K(Key.Up), K(Key.PageUp), K(Key.Back)]),
        new("first", "이동", "첫 슬라이드", true, [K(Key.Home)]),
        new("last", "이동", "마지막 슬라이드", true, [K(Key.End)]),
        new("select", "판서", "선택/포인터", true, []),
        new("pen", "판서", "펜", true, [K(Key.P)]),
        new("highlighter", "판서", "형광펜", true, [K(Key.H)]),
        new("eraser", "판서", "지우개", true, [K(Key.E)]),
        new("laser", "판서", "레이저 포인터", true, [K(Key.L, Ctrl)]),
        .. PenState.Palette.Select((_, i) => new ShortcutAction($"color{i + 1}", "판서", $"색 {i + 1} ({ColorNames[i]})", true, [K(Key.D1 + i), K(Key.NumPad1 + i)])),
        new("thinner", "판서", "가늘게", true, [K(Key.OemOpenBrackets), K(Key.OemMinus), K(Key.Subtract)]),
        new("thicker", "판서", "굵게", true, [K(Key.OemCloseBrackets), K(Key.OemPlus), K(Key.Add)]),
        new("clearInk", "판서", "현재 슬라이드 판서 지우기", true, [K(Key.C)]),
        new("blankPage", "판서", "빈 판서 페이지 삽입", true, [K(Key.Enter, Ctrl)]),
        new("showFirst", "쇼", "처음부터 슬라이드 쇼", false, [K(Key.F5)]),
        new("showCurrent", "쇼", "현재 슬라이드부터 슬라이드 쇼", false, [K(Key.F5, ModifierKeys.Shift)]),
        new("break", "쇼", "Break Time 타이머", true, [K(Key.T)]),
        new("black", "쇼", "검은 화면", true, [K(Key.B), K(Key.OemPeriod)]),
        new("white", "쇼", "흰 화면", true, [K(Key.W), K(Key.OemComma)]),
        new("zoom", "쇼", "화면 확대 (영역 드래그)", true, [K(Key.Z)]),
        new("open", "파일", "열기", false, [K(Key.O, Ctrl)]),
        new("save", "파일", "저장", false, [K(Key.S, Ctrl)]),
        new("saveAs", "파일", "다른 이름으로 저장", false, [K(Key.S, CtrlShift)]),
        new("exportPdf", "파일", "PDF로 내보내기", false, []),
        new("undo", "편집", "실행 취소 (판서 우선)", true, [K(Key.Z, Ctrl)]),
        new("editText", "편집", "텍스트 편집", false, [K(Key.F2)]),
        new("deleteShape", "편집", "개체 삭제", false, [K(Key.Delete)]),
        new("bold", "편집", "굵게", false, [K(Key.B, Ctrl)]),
        new("italic", "편집", "기울임꼴", false, [K(Key.I, Ctrl)]),
        new("underline", "편집", "밑줄", false, [K(Key.U, Ctrl)]),
        new("grow", "편집", "글꼴 크기 크게", false, [K(Key.OemPeriod, CtrlShift)]),
        new("shrink", "편집", "글꼴 크기 작게", false, [K(Key.OemComma, CtrlShift)]),
        new("newSlide", "슬라이드", "새 슬라이드", false, [K(Key.M, Ctrl)]),
        new("duplicate", "슬라이드", "슬라이드 복제", false, [K(Key.D, Ctrl)]),
        new("help", "도움말", "단축키 목록", false, [K(Key.F1)]),
        new("settings", "도움말", "단축키 설정", false, []),
    ];

    static string[] ColorNames => ["검정", "빨강", "파랑", "초록", "노랑", "흰색", "주황"];

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PptSlider", "shortcuts.json");

    static Dictionary<string, List<Gesture>> map = Defaults();

    /// <summary>Raised after the bindings change, so tooltips and hints can refresh.</summary>
    public static event Action? Changed;

    public static Dictionary<string, List<Gesture>> Defaults() => All.ToDictionary(a => a.Id, a => a.Defaults.ToList());

    /// <summary>A deep copy for the settings dialog to edit.</summary>
    public static Dictionary<string, List<Gesture>> Copy() => map.ToDictionary(p => p.Key, p => p.Value.ToList());

    public static IReadOnlyList<Gesture> Of(string id) => map.TryGetValue(id, out var g) ? g : [];

    /// <summary>"Ctrl+O", "→ / ↓ / ..." or "" when unassigned.</summary>
    public static string Text(string id) => string.Join(" / ", Of(id));

    /// <summary>The command bound to <paramref name="g"/>; with <paramref name="inShow"/>, only slide show commands.</summary>
    public static string? Find(Gesture g, bool inShow) =>
        All.FirstOrDefault(a => (!inShow || a.InShow) && Of(a.Id).Contains(g))?.Id;

    public static void Set(Dictionary<string, List<Gesture>> m)
    {
        map = m;
        Changed?.Invoke();
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(FilePath));
            if (saved == null) return;
            var m = Defaults(); // commands added after the file was written keep their defaults
            foreach (var (id, keys) in saved)
                if (m.ContainsKey(id)) m[id] = keys.Select(Gesture.Parse).OfType<Gesture>().Distinct().ToList();
            Set(m);
        }
        catch (Exception) { } // unreadable file: keep the defaults
    }

    public static bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var data = map.ToDictionary(p => p.Key, p => p.Value.Select(g => g.Serialize()).ToArray());
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception) { return false; }
    }

    // ---------- tooltips ----------

    /// <summary>
    /// <c>local:Shortcuts.For="save"</c> on a control appends the current binding to its ToolTip,
    /// e.g. "저장" → "저장 (Ctrl+S)", and keeps it up to date when the bindings change.
    /// </summary>
    public static readonly DependencyProperty ForProperty = DependencyProperty.RegisterAttached(
        "For", typeof(string), typeof(Shortcuts), new PropertyMetadata(null, OnForChanged));

    public static string? GetFor(DependencyObject d) => (string?)d.GetValue(ForProperty);
    public static void SetFor(DependencyObject d, string? v) => d.SetValue(ForProperty, v);

    static readonly DependencyProperty BaseTipProperty = DependencyProperty.RegisterAttached("BaseTip", typeof(string), typeof(Shortcuts));

    static void OnForChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || e.OldValue != null) return;
        void Update()
        {
            if (fe.GetValue(BaseTipProperty) is not string tip) fe.SetValue(BaseTipProperty, tip = fe.ToolTip as string ?? "");
            var keys = Text(GetFor(fe)!);
            fe.ToolTip = keys == "" ? tip : $"{tip} ({keys})";
        }
        fe.Loaded += (_, _) => { Changed -= Update; Changed += Update; Update(); };
        fe.Unloaded += (_, _) => Changed -= Update;
    }
}
