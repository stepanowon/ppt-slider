using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Ink;

namespace PptSlider;

/// <summary>
/// Keeps each deck's ink outside the deck, so it survives closing the app without saving:
/// %LOCALAPPDATA%\PptSlider\ink\&lt;hash of the file path&gt;.isf, one ISF stroke collection whose strokes carry their slide's id.
/// </summary>
public static class InkStore
{
    static readonly Guid SlideIdKey = new("6f1c3c2e-7a4b-4f53-9a51-2b8f6c0d1e47");

    /// <summary>The folder; the self-test points it elsewhere.</summary>
    internal static string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PptSlider", "ink");

    public static string FileFor(string deckPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(deckPath).ToUpperInvariant()));
        return Path.Combine(Folder, Convert.ToHexString(hash)[..16] + ".isf");
    }

    /// <summary>Writes the strokes per slide id; no strokes at all removes the file.</summary>
    public static void Save(string deckPath, IEnumerable<(uint SlideId, StrokeCollection Strokes)> slides)
    {
        var all = new StrokeCollection();
        foreach (var (id, strokes) in slides)
            foreach (var s in strokes)
            {
                var copy = s.Clone();
                copy.AddPropertyData(SlideIdKey, (int)id);
                all.Add(copy);
            }
        var file = FileFor(deckPath);
        if (all.Count == 0)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }
        Directory.CreateDirectory(Folder);
        var tmp = file + ".tmp";
        using (var f = File.Create(tmp)) all.Save(f, compress: true);
        File.Move(tmp, file, true);
    }

    /// <summary>The kept strokes by slide id; empty when there are none or the file can't be read.</summary>
    public static Dictionary<uint, StrokeCollection> Load(string deckPath)
    {
        var result = new Dictionary<uint, StrokeCollection>();
        try
        {
            var file = FileFor(deckPath);
            if (!File.Exists(file)) return result;
            using var f = File.OpenRead(file);
            foreach (var s in new StrokeCollection(f))
            {
                if (!s.ContainsPropertyData(SlideIdKey) || s.GetPropertyData(SlideIdKey) is not int id) continue;
                s.RemovePropertyData(SlideIdKey);
                if (!result.TryGetValue((uint)id, out var list)) result[(uint)id] = list = new StrokeCollection();
                list.Add(s);
            }
        }
        catch (Exception) { result.Clear(); } // a damaged file: start without ink
        return result;
    }
}
