using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;

namespace PptSlider;

/// <summary>New objects on the current slide: text box, picture (bytes, file or the clipboard), table. Each is selected afterwards.</summary>
sealed class InsertCommands(IEditorHost host, ShapeSelection selection, TextEditor text, SlideEdits edits)
{
    /// <summary>A text box in the middle of the slide holding <paramref name="content"/>; <paramref name="edit"/> opens it for typing.</summary>
    public void TextBox(string content, Rect where, bool edit)
    {
        if (host.Deck == null) return;
        text.Commit();
        host.UseSelectTool();
        edits.Apply(() =>
        {
            selection.Primary = ShapeFactory.AddTextBox(host.CurrentSlide!, where, edit ? content : "");
            if (!edit) TextContent.SetText(selection.Primary, content);
        });
        if (edit) text.Begin();
    }

    public bool PictureFile(string path)
    {
        if (ShapeFactory.PictureType(Path.GetExtension(path)) is not { } type) return false;
        try { return Picture(File.ReadAllBytes(path), type); }
        catch (Exception ex) { host.Warn(L.F("pictureFail", ex.Message), L.T("picture")); return false; }
    }

    /// <summary>A picture on the clipboard (copied image, or an image file copied in Explorer) goes on the slide.</summary>
    public bool PictureFromClipboard()
    {
        if (host.Deck == null) return false;
        try
        {
            if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(f => ShapeFactory.PictureType(Path.GetExtension(f)) != null) is { } f)
                return PictureFile(f);
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } img) return false;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return Picture(ms.ToArray(), ShapeFactory.PictureType(".png")!.Value);
        }
        catch (System.Runtime.InteropServices.ExternalException) { return false; } // the clipboard is busy
    }

    /// <summary>Centered on the current slide at its own size, shrunk to fit 60% of the slide; selected afterwards.</summary>
    public bool Picture(byte[] image, PartTypeInfo type)
    {
        if (host.Deck is not { } deck || host.CurrentSlide is not { } sp) return false;
        BitmapFrame frame;
        try { frame = BitmapDecoder.Create(new MemoryStream(image), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; }
        catch (Exception ex) { host.Warn(L.F("pictureFail", ex.Message), L.T("picture")); return false; }
        text.Commit();
        host.UseSelectTool();
        double sw = deck.Renderer.Width, sh = deck.Renderer.Height;
        double w = frame.PixelWidth, h = frame.PixelHeight;
        double s = Math.Min(1, Math.Min(sw * 0.6 / w, sh * 0.6 / h));
        w *= s; h *= s;
        edits.Apply(() => selection.Primary = ShapeFactory.AddPicture(sp, image, type, new Rect((sw - w) / 2, (sh - h) / 2, w, h)));
        return true;
    }

    /// <summary>A table across 80% of the slide's width, selected, its first cell current.</summary>
    public void Table(int rows, int cols)
    {
        if (host.Deck is not { } deck) return;
        text.Commit();
        host.UseSelectTool();
        double w = deck.Renderer.Width, h = deck.Renderer.Height;
        edits.Apply(() =>
        {
            var frame = Tables.AddTable(host.CurrentSlide!, rows, cols, new Rect(w * 0.1, h * 0.25, w * 0.8, 0));
            selection.Primary = frame;
            selection.Cell = Tables.Cell(frame, 0, 0);
        });
    }
}
