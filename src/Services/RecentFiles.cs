using System.IO;

namespace PptSlider;

/// <summary>The last 10 opened files, newest first, in %LOCALAPPDATA%\PptSlider\recent.txt (one path per line).</summary>
public static class RecentFiles
{
    const int Max = 10;
    static string FilePath => Path.Combine(AppPaths.Local, "recent.txt");

    public static List<string> Load()
    {
        try { return File.Exists(FilePath) ? File.ReadAllLines(FilePath).Where(l => l.Trim() != "").Take(Max).ToList() : []; }
        catch (Exception) { return []; }
    }

    public static void Add(string path)
    {
        var full = Path.GetFullPath(path);
        var list = Load().Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase)).Prepend(full).Take(Max).ToList();
        try
        {
            Directory.CreateDirectory(AppPaths.Local);
            File.WriteAllLines(FilePath, list);
        }
        catch (Exception) { } // a list we can't write only costs the convenience
    }
}
