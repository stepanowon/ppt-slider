using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;

namespace PptSlider;

/// <summary>
/// Slides as a PDF of pictures: each page is one JPEG of the slide (and its ink), at twice the screen size.
/// Hand-written minimal PDF, so no package is needed; the text can't be selected.
/// </summary>
public static class PdfExport
{
    public static void Export(Deck deck, string path, IReadOnlyDictionary<SlidePart, StrokeCollection>? ink)
    {
        double w = deck.Renderer.Width, h = deck.Renderer.Height;
        var pages = deck.Slides.Select(sp =>
        {
            var el = deck.Renderer.Render(sp);
            el.Measure(new Size(w, h));
            el.Arrange(new Rect(0, 0, w, h));
            el.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
            var white = new DrawingVisual();
            using (var dc = white.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h)); // JPEG has no transparency
            bmp.Render(white);
            bmp.Render(el);
            if (ink != null && ink.TryGetValue(sp, out var strokes) && strokes.Count > 0) bmp.Render(Deck.InkVisual(strokes));
            var enc = new JpegBitmapEncoder { QualityLevel = 90 };
            enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0)));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (Jpeg: ms.ToArray(), Px: (bmp.PixelWidth, bmp.PixelHeight));
        }).ToList();
        var tmp = path + ".tmp";
        using (var f = File.Create(tmp)) Write(f, pages.Select(p => (p.Jpeg, p.Px.PixelWidth, p.Px.PixelHeight)).ToList(), w * 72 / 96, h * 72 / 96);
        File.Move(tmp, path, true);
    }

    /// <summary>Catalog, page tree, then per page: page, content stream, image; then the xref table.</summary>
    static void Write(Stream s, List<(byte[] Jpeg, int W, int H)> pages, double ptW, double ptH)
    {
        var offsets = new List<long>();
        void Text(string t) { var b = Encoding.ASCII.GetBytes(t); s.Write(b); }
        void Obj(string body, byte[]? stream = null)
        {
            offsets.Add(s.Position);
            Text($"{offsets.Count} 0 obj\n{body}\n");
            if (stream != null) { Text("stream\n"); s.Write(stream); Text("\nendstream\n"); }
            Text("endobj\n");
        }
        string size = FormattableString.Invariant($"{ptW:0.##} {ptH:0.##}");

        Text("%PDF-1.4\n");
        s.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']); // marks the file as binary
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Kids [{string.Join(" ", pages.Select((_, i) => $"{3 + i * 3} 0 R"))}] /Count {pages.Count} >>");
        for (int i = 0; i < pages.Count; i++)
        {
            int page = 3 + i * 3;
            var draw = Encoding.ASCII.GetBytes($"q {size.Replace(" ", " 0 0 ")} 0 0 cm /Im0 Do Q");
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {size}] /Resources << /XObject << /Im0 {page + 2} 0 R >> >> /Contents {page + 1} 0 R >>");
            Obj($"<< /Length {draw.Length} >>", draw);
            Obj($"<< /Type /XObject /Subtype /Image /Width {pages[i].W} /Height {pages[i].H} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {pages[i].Jpeg.Length} >>", pages[i].Jpeg);
        }
        long xref = s.Position;
        Text($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Text($"{o:D10} 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
