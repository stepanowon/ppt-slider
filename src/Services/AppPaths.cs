using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PptSlider;

/// <summary>Where the app keeps its own files: recent files, autosaved copies.</summary>
public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\PptSlider; the self-test points it at its output folder.</summary>
    internal static string Local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PptSlider");

    /// <summary>A file name for <paramref name="deckPath"/>: the same file always gets the same key.</summary>
    public static string Key(string deckPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(deckPath).ToUpperInvariant()));
        return Convert.ToHexString(hash)[..16];
    }
}
