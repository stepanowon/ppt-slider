using System.Windows;

namespace PptSlider;

public partial class App : Application
{
    void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args is ["--selftest", var file, var outDir])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown; // the test's own windows closing must not end it with code 0
            Shutdown(SelfTest.Run(file, outDir));
            return;
        }
        Shortcuts.Load();
        var w = new MainWindow();
        w.Show();
        if (e.Args.FirstOrDefault() is { } path) w.Open(path); // file association / "Open with"
    }
}
