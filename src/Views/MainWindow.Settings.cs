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

/// <summary>Help, shortcut settings, UI language.</summary>
public partial class MainWindow
{
    void OnHelp(object? sender, RoutedEventArgs? e)
    {
        var lines = Shortcuts.All.GroupBy(a => a.Group).Select(g =>
            $"[{g.Key}]\n" + string.Join("\n", g.Where(a => Shortcuts.Of(a.Id).Count > 0).Select(a => $"    {a.Name} : {Shortcuts.Text(a.Id)}")));
        MessageBox.Show(this, string.Join("\n", lines) + "\n\n" + L.T("helpFixed"), L.T("shortcuts"));
    }

    void OnShortcutSettings(object? sender, RoutedEventArgs? e) => new ShortcutsWindow { Owner = this }.ShowDialog();

    /// <summary>The 설정 tab's language list: same as Windows (the default), then 한국어 / 日本語 / English.</summary>
    void FillLanguages()
    {
        syncing = true;
        LanguageBox.ItemsSource = new[] { L.F("languageSystem", L.Names[Array.IndexOf(L.Languages, L.WindowsLang)]) }.Concat(L.Names).ToList();
        LanguageBox.SelectedIndex = L.Saved is { } s ? Array.IndexOf(L.Languages, s) + 1 : 0;
        syncing = false;
    }

    void OnLanguagePicked(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || LanguageBox.SelectedIndex < 0) return;
        PickLanguage(LanguageBox.SelectedIndex == 0 ? null : L.Languages[LanguageBox.SelectedIndex - 1]);
    }

    /// <summary>Saves the choice (null: same as Windows); it applies after a restart, offered right away.</summary>
    void PickLanguage(string? lang)
    {
        if (lang == L.Saved) return;
        if (!L.Save(lang))
        {
            MessageBox.Show(this, L.T("languageSaveFail"), L.T("language"), MessageBoxButton.OK, MessageBoxImage.Warning);
            FillLanguages();
            return;
        }
        var next = lang ?? L.WindowsLang;
        if (next == L.Lang) return; // e.g. picked Windows' language, which is already showing
        if (MessageBox.Show(this, L.T("languageRestart", next), "PPT Slider", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes
            || !ConfirmDiscard()) return; // the choice still applies at the next start
        var path = Deck?.Path;
        restarting = true; // already asked about unsaved edits
        System.Diagnostics.Process.Start(App.RestartInfo(path));
        Close();
    }

    bool restarting;

    void UpdateHint() =>
        EmptyHint.Text = L.T("emptyHint") + (Shortcuts.Text("open") is { Length: > 0 } k ? $" ({k})" : "");
}
