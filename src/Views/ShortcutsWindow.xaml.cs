using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PptSlider;

/// <summary>Edits a copy of the key bindings; OK applies and saves them.</summary>
public partial class ShortcutsWindow : Window
{
    public record Row(ShortcutAction Action, string Group, string Name, string Keys);

    readonly Dictionary<string, List<Gesture>> map = Shortcuts.Copy();
    bool? capturing; // null: idle, true: add a key, false: replace the keys

    public ShortcutsWindow()
    {
        InitializeComponent();
        Refresh();
        List.SelectedIndex = 0;
    }

    ShortcutAction? Selected => (List.SelectedItem as Row)?.Action;

    void Refresh()
    {
        int i = List.SelectedIndex;
        List.ItemsSource = Shortcuts.All.Select(a => new Row(a, a.Group, a.Name, string.Join(" / ", map[a.Id]))).ToList();
        List.SelectedIndex = i;
        if (List.SelectedItem != null) List.ScrollIntoView(List.SelectedItem);
        OnSelected(null, null);
    }

    void OnSelected(object? sender, SelectionChangedEventArgs? e)
    {
        bool has = Selected != null;
        AddBtn.IsEnabled = ReplaceBtn.IsEnabled = DefaultBtn.IsEnabled = has;
        ClearBtn.IsEnabled = has && map[Selected!.Id].Count > 0;
    }

    void BeginCapture(bool add)
    {
        if (Selected is not { } a) return;
        capturing = add;
        Message.Text = "";
        CaptureText.Text = L.F(add ? "pressKeyToAdd" : "pressKeyToSet", a.Name);
        CaptureBar.Visibility = Visibility.Visible;
        List.IsEnabled = Buttons.IsEnabled = false; // also keeps Enter / Esc from reaching OK / Cancel
        Keyboard.Focus(this);
    }

    void EndCapture()
    {
        capturing = null;
        CaptureBar.Visibility = Visibility.Collapsed;
        List.IsEnabled = Buttons.IsEnabled = true;
        List.Focus();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (capturing is not { } add) return; // normal dialog keys (Enter = OK, Esc = cancel, arrows in the list)
        e.Handled = true;
        var g = Gesture.From(e);
        if (Gesture.IsModifierKey(g.Key)) return; // wait for the actual key
        if (g is { Key: Key.Escape, Mods: ModifierKeys.None }) { EndCapture(); return; }
        if (g.Key == Key.Escape) { Message.Text = L.T("escReserved"); EndCapture(); return; }

        var a = Selected!;
        var owner = Shortcuts.All.FirstOrDefault(o => o != a && map[o.Id].Contains(g));
        if (owner != null)
        {
            if (MessageBox.Show(this, L.F("keyTaken", g, owner.Name, a.Name), L.T("keyTakenTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { EndCapture(); return; }
            map[owner.Id].Remove(g);
        }
        if (!add) map[a.Id].Clear();
        if (!map[a.Id].Contains(g)) map[a.Id].Add(g);
        EndCapture();
        Refresh();
    }

    void OnAdd(object sender, RoutedEventArgs e) => BeginCapture(true);
    void OnReplace(object sender, RoutedEventArgs e) { if (capturing == null) BeginCapture(false); }

    void OnClear(object sender, RoutedEventArgs e)
    {
        if (Selected is { } a) { map[a.Id].Clear(); Refresh(); }
    }

    void OnResetOne(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } a) return;
        foreach (var g in a.Defaults) // a default taken back from another command
            foreach (var other in Shortcuts.All.Where(o => o != a)) map[other.Id].Remove(g);
        map[a.Id] = a.Defaults.ToList();
        Refresh();
    }

    void OnResetAll(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, L.T("resetAllAsk"), L.T("shortcutSettings"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        foreach (var (id, keys) in Shortcuts.Defaults()) map[id] = keys;
        Refresh();
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        Shortcuts.Set(map);
        if (!Shortcuts.Save())
            MessageBox.Show(this, L.T("settingsSaveFail"), L.T("shortcutSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
        DialogResult = true;
    }
}
