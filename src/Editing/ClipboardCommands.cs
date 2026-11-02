using System.Runtime.InteropServices;
using System.Windows;

namespace PptSlider;

/// <summary>
/// Cut, copy, paste and duplicate of shapes. Copied shapes stay in memory; the system clipboard gets a token naming them
/// (and their text), so pasting knows whether what is on the clipboard is still ours.
/// </summary>
sealed class ClipboardCommands(IEditorHost host, ShapeSelection selection, TextEditor text, SlideEdits edits, InsertCommands insert)
{
    const string ClipFormat = "PptSlider.Shapes";
    ShapeClip? clip;
    string clipToken = "";
    bool clipCut;
    int pasteCount;

    /// <summary>A new file was opened: shapes copied from the old one can't be pasted into it.</summary>
    public void Forget() => clip = null;

    public void Copy(bool cut)
    {
        if (host.Deck is not { } deck || selection.Count == 0) { host.Status(L.T("selectShapeFirst")); return; }
        text.Commit();
        var picked = selection.Hits;
        clip = ShapeClipboard.Copy(deck, host.CurrentSlide!, picked.Select(h => (h.Element, h.Bounds)));
        clipToken = Guid.NewGuid().ToString();
        clipCut = cut;
        pasteCount = 0;
        var words = string.Join("\r\n", picked.SelectMany(h => TextFormat.Paragraphs(h.Element)).Select(p => p.InnerText).Where(t => t.Length > 0));
        try
        {
            var data = new DataObject();
            data.SetData(ClipFormat, clipToken);
            if (words.Length > 0) data.SetText(words);
            Clipboard.SetDataObject(data, true);
        }
        catch (ExternalException) { } // the clipboard is busy: pasting here still works
        if (!cut) return;
        edits.Apply(() =>
        {
            foreach (var h in picked) h.Element.Remove();
            selection.Primary = null;
        });
    }

    /// <summary>Ctrl+V: shapes copied here, else a picture, else text (as a new text box).</summary>
    public bool Paste()
    {
        if (host.Deck is not { } deck) return false;
        string? token = null, words = null;
        try
        {
            if (Clipboard.ContainsData(ClipFormat)) token = Clipboard.GetData(ClipFormat) as string;
            else if (Clipboard.ContainsText()) words = Clipboard.GetText();
        }
        catch (ExternalException) { token = clipToken; } // busy: assume it is still ours
        if (clip != null && token == clipToken) return PasteShapes();
        if (insert.PictureFromClipboard()) return true;
        if (!string.IsNullOrWhiteSpace(words))
        {
            double w = deck.Renderer.Width, h = deck.Renderer.Height;
            insert.TextBox(words.Replace("\r\n", "\n").TrimEnd('\n'), new Rect(w * 0.25, h * 0.4, w * 0.5, h * 0.12), edit: false);
            return true;
        }
        return clip != null && PasteShapes(); // nothing else usable: what was copied here
    }

    /// <summary>Pastes the copied shapes; on their own slide each paste lands a little lower right, as in PowerPoint.</summary>
    bool PasteShapes()
    {
        if (host.Deck is not { } deck || clip == null) return false;
        text.Commit();
        int n = pasteCount++;
        double off = clip.From == host.CurrentSlide ? (clipCut ? n : n + 1) * 10 : 0;
        deck.History.Snapshot(host.CurrentSlide!);
        var added = ShapeClipboard.Paste(deck, host.CurrentSlide!, clip, off, off);
        if (added.Count == 0) { host.Status(L.T("nothingToPaste")); return false; }
        host.UseSelectTool();
        selection.Set(added);
        host.Changed();
        return true;
    }

    /// <summary>Ctrl+D with shapes selected: copies of them right next to them.</summary>
    public void Duplicate()
    {
        if (host.Deck is not { } deck || selection.Count == 0) return;
        text.Commit();
        var copy = ShapeClipboard.Copy(deck, host.CurrentSlide!, selection.Hits.Select(h => (h.Element, h.Bounds)));
        edits.Apply(() => selection.Set(ShapeClipboard.Paste(deck, host.CurrentSlide!, copy, 10, 10)));
    }
}
