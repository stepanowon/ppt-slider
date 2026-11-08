using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace PptSlider;

/// <summary>The app's name, version, copyright and license, its links, and the system it runs on (for bug reports).</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = L.F("versionOf", Version);
        CopyrightText.Text = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        SystemText.Text = SystemInfo;
        Logo.Source = LargestIcon();
        // the third-party notices ship next to the exe (and in the MSIX); without the file the link is left out
        var notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(notices)) NoticesLink.NavigateUri = new Uri(notices);
        else NoticesLine.Visibility = Visibility.Collapsed;
    }

    /// <summary>The product version without the build's commit hash ("0.2.6" of "0.2.6+05c64f4...").</summary>
    internal static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            return v.Split('+')[0];
        }
    }

    /// <summary>What a bug report needs: version, .NET, Windows and the language shown.</summary>
    internal static string SystemInfo =>
        $"{RuntimeInformation.FrameworkDescription} · Windows {Environment.OSVersion.Version} · {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()} · {L.Lang}";

    /// <summary>The app icon's largest frame (the .ico holds several sizes; WPF would otherwise pick the first).</summary>
    static BitmapSource? LargestIcon()
    {
        try
        {
            using var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PptSlider.ico"))?.Stream;
            if (s == null) return null;
            var d = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return d.Frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch (Exception) { return null; } // no logo is not worth an error
    }

    void OnLink(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show(this, e.Uri.AbsoluteUri, L.T("aboutTitle")); } // no browser: show the address to copy
        e.Handled = true;
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText($"PPT Slider {Version}\n{SystemInfo}");
            CopyBtn.Content = L.T("copied");
        }
        catch (Exception) { } // the clipboard can be busy; the button just stays as it is
    }
}
