namespace PptSlider;

static partial class SelfTest
{
    /// <summary>
    /// The restart after a language change, end to end on its arguments: with no file open it used to pass "" and the new
    /// app then tried to open "" ("The path is empty"). A real file, spaces and all, still comes back open.
    /// </summary>
    static void StartupArgTests(Action<bool, string> check)
    {
        string[] Args(string? path) => [.. App.RestartInfo(path).ArgumentList];
        check(Args(null).Length == 0 && Args("").Length == 0 && Args("  ").Length == 0, "startup: a restart with no file open passes no argument");
        check(App.FileToOpen(Args(null)) == null, "startup: so the restarted app opens no file (no \"path is empty\")");
        check(App.FileToOpen([]) == null && App.FileToOpen([""]) == null && App.FileToOpen(["   "]) == null,
            "startup: a blank argument (from an older restart) is not a file to open");
        const string deck = @"C:\내 문서\발표 자료 1.pptx";
        check(Args(deck) is [deck] && App.FileToOpen(Args(deck)) == deck, "startup: a restart with a file open opens it again (spaces in the path kept)");
    }

    /// <summary>The About window: the version of this build, the copyright, the icon, and the third-party notices next to the exe.</summary>
    static void AboutTests(string outDir, Action<bool, string> check)
    {
        var w = new AboutWindow { WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = 2000, ShowActivated = false };
        try
        {
            w.Show();
            var version = AboutWindow.Version;
            check(System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") && w.VersionText.Text == L.F("versionOf", version),
                $"about: shows this build's version ({w.VersionText.Text})");
            check(w.CopyrightText.Text.Contains("StephenWon") && w.Logo.Source is { Width: >= 64 } && w.SystemText.Text.Contains(".NET"),
                "about: copyright, the app icon (its largest size) and the system details");
            check(w.NoticesLink.NavigateUri is { IsFile: true } u && System.IO.File.Exists(u.LocalPath), "about: the third-party notices link opens the file next to the exe");
            Snap(w.Content as System.Windows.FrameworkElement ?? w, System.IO.Path.Combine(outDir, "about.png"));
        }
        finally { w.Close(); }
    }
}
