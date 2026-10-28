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
        new("next", L.T("g.move"), L.T("nextSlide"), true, [K(Key.Right), K(Key.Down), K(Key.PageDown), K(Key.Space), K(Key.Enter), K(Key.N)]),
        new("prev", L.T("g.move"), L.T("prevSlide"), true, [K(Key.Left), K(Key.Up), K(Key.PageUp), K(Key.Back)]),
        new("first", L.T("g.move"), L.T("firstSlide"), true, [K(Key.Home)]),
        new("last", L.T("g.move"), L.T("lastSlide"), true, [K(Key.End)]),
        new("select", L.T("g.ink"), L.T("selectPointer"), true, []),
        new("pen", L.T("g.ink"), L.T("pen"), true, [K(Key.P)]),
        new("highlighter", L.T("g.ink"), L.T("highlighter"), true, [K(Key.H)]),
        new("eraser", L.T("g.ink"), L.T("eraser"), true, [K(Key.E)]),
        new("laser", L.T("g.ink"), L.T("laser"), true, [K(Key.L, Ctrl)]),
        .. PenState.Palette.Select((_, i) => new ShortcutAction($"color{i + 1}", L.T("g.ink"), L.F("colorN", i + 1, ColorNames[i]), true, [K(Key.D1 + i), K(Key.NumPad1 + i)])),
        new("thinner", L.T("g.ink"), L.T("thinner"), true, [K(Key.OemOpenBrackets), K(Key.OemMinus), K(Key.Subtract)]),
        new("thicker", L.T("g.ink"), L.T("thicker"), true, [K(Key.OemCloseBrackets), K(Key.OemPlus), K(Key.Add)]),
        new("clearInk", L.T("g.ink"), L.T("clearInk"), true, [K(Key.C)]),
        new("blankPage", L.T("g.ink"), L.T("insertBlankPage"), true, [K(Key.Enter, Ctrl)]),
        new("showFirst", L.T("g.show"), L.T("showFromStart"), false, [K(Key.F5)]),
        new("showCurrent", L.T("g.show"), L.T("showFromCurrent"), false, [K(Key.F5, ModifierKeys.Shift)]),
        new("break", L.T("g.show"), L.T("breakTimer"), true, [K(Key.T)]),
        new("black", L.T("g.show"), L.T("black"), true, [K(Key.B), K(Key.OemPeriod)]),
        new("white", L.T("g.show"), L.T("white"), true, [K(Key.W), K(Key.OemComma)]),
        new("zoom", L.T("g.show"), L.T("zoom"), true, [K(Key.Z)]),
        new("open", L.T("g.file"), L.T("open"), false, [K(Key.O, Ctrl)]),
        new("save", L.T("g.file"), L.T("save"), false, [K(Key.S, Ctrl)]),
        new("saveAs", L.T("g.file"), L.T("saveAs"), false, [K(Key.S, CtrlShift)]),
        new("exportPdf", L.T("g.file"), L.T("exportPdf"), false, []),
        new("undo", L.T("g.edit"), L.T("undoInkFirst"), true, [K(Key.Z, Ctrl)]),
        new("editText", L.T("g.edit"), L.T("editText"), false, [K(Key.F2)]),
        new("deleteShape", L.T("g.edit"), L.T("deleteShape"), false, [K(Key.Delete)]),
        new("bold", L.T("g.edit"), L.T("bold"), false, [K(Key.B, Ctrl)]),
        new("italic", L.T("g.edit"), L.T("italic"), false, [K(Key.I, Ctrl)]),
        new("underline", L.T("g.edit"), L.T("underline"), false, [K(Key.U, Ctrl)]),
        new("grow", L.T("g.edit"), L.T("grow"), false, [K(Key.OemPeriod, CtrlShift)]),
        new("shrink", L.T("g.edit"), L.T("shrink"), false, [K(Key.OemComma, CtrlShift)]),
        new("newSlide", L.T("g.slides"), L.T("newSlide"), false, [K(Key.M, Ctrl)]),
        new("duplicate", L.T("g.slides"), L.T("duplicateSlide"), false, [K(Key.D, Ctrl)]),
        new("help", L.T("g.help"), L.T("shortcutList"), false, [K(Key.F1)]),
        new("settings", L.T("g.help"), L.T("shortcutSettings"), false, []),
    ];

    static string[] ColorNames => L.T("colorNames").Split(',');

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
    /// e.g. "Save" → "Save (Ctrl+S)", and keeps it up to date when the bindings change.
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
