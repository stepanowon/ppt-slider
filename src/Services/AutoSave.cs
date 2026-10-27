using System.IO;

namespace PptSlider;

/// <summary>
/// Copies of decks with unsaved edits, %LOCALAPPDATA%\PptSlider\autosave\&lt;key&gt;.pptx, written every 2 minutes.
/// A normal close removes the copy, so one that is still there and newer than its file means the app did not get to save.
/// </summary>
public static class AutoSave
{
    static string Folder => Path.Combine(AppPaths.Local, "autosave");

    public static string FileFor(string deckPath) => Path.Combine(Folder, AppPaths.Key(deckPath) + ".pptx");

    public static void Save(Deck deck)
    {
        Directory.CreateDirectory(Folder);
        deck.SaveCopy(FileFor(deck.Path));
    }

    public static void Delete(string deckPath)
    {
        try { if (File.Exists(FileFor(deckPath))) File.Delete(FileFor(deckPath)); }
        catch (Exception) { }
    }

    /// <summary>The copy to offer when opening <paramref name="deckPath"/>, or null.</summary>
    public static string? Recoverable(string deckPath)
    {
        var copy = FileFor(deckPath);
        return File.Exists(copy) && File.GetLastWriteTimeUtc(copy) > File.GetLastWriteTimeUtc(deckPath) ? copy : null;
    }
}
