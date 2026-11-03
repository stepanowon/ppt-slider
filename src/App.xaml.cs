using System.Windows;

namespace PptSlider;

public partial class App : Application
{
    void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args is ["--selftest", var file, var outDir])
        {
            L.Init("ko"); // the checks compare Korean text
            ShutdownMode = ShutdownMode.OnExplicitShutdown; // the test's own windows closing must not end it with code 0
            Shutdown(SelfTest.Run(file, outDir));
            return;
        }
        if (e.Args is ["--screenshots", var deck, var dir, ..] && e.Args.Length <= 5)
        {
            L.Init(e.Args.Length > 3 ? e.Args[3] : null);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(SelfTest.Screens(deck, dir, e.Args.Length > 4 ? int.Parse(e.Args[4]) : 0));
            return;
        }
        if (e.Args is ["--render", var src, var to, ..] && e.Args.Length <= 4)
        {
            L.Init("ko");
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(SelfTest.RenderAll(src, to, e.Args.Length > 3 ? int.Parse(e.Args[3]) : 1280));
            return;
        }
        L.Init();
        Shortcuts.Load();
        var w = new MainWindow();
        w.Show();
        if (FileToOpen(e.Args) is { } path) w.Open(path); // file association / "Open with" / restart after a language change
    }

    /// <summary>The file named on the command line, if any: a blank argument (an older restart passed one) is no file.</summary>
    internal static string? FileToOpen(string[] args) => args.FirstOrDefault() is { } a && !string.IsNullOrWhiteSpace(a) ? a : null;

    /// <summary>Starts the app again, with <paramref name="path"/> open in it when there is one.</summary>
    internal static System.Diagnostics.ProcessStartInfo RestartInfo(string? path)
    {
        var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (!string.IsNullOrWhiteSpace(path)) info.ArgumentList.Add(path); // no file open: no argument at all
        return info;
    }
}
