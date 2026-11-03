using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;

namespace PptSlider;

/// <summary>`PptSlider.exe --render deck.pptx outDir [width]`: every slide as outDir/NN.png, to compare with PowerPoint's export.</summary>
static partial class SelfTest
{
    public static int RenderAll(string file, string outDir, int width)
    {
        Directory.CreateDirectory(outDir);
        using var doc = PresentationDocument.Open(file, false);
        var pres = doc.PresentationPart!;
        var r = new SlideRenderer(pres);
        int n = 0;
        foreach (var id in pres.Presentation!.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>())
        {
            n++;
            var canvas = r.Render((SlidePart)pres.GetPartById(id.RelationshipId!));
            canvas.Measure(new Size(r.Width, r.Height));
            canvas.Arrange(new Rect(0, 0, r.Width, r.Height));
            canvas.UpdateLayout();
            double s = width / r.Width;
            var bmp = new RenderTargetBitmap(width, (int)Math.Round(r.Height * s), 96 * s, 96 * s, PixelFormats.Pbgra32);
            bmp.Render(canvas);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var f = File.Create(Path.Combine(outDir, $"{n:00}.png"));
            enc.Save(f);
        }
        return 0;
    }
}
