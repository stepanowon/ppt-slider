using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace PptSlider;

/// <summary>
/// `PptSlider.exe --selftest deck.pptx outDir`: renders every slide to PNG, then checks the
/// edit → save (with / without ink) → reopen round trip. Writes outDir/selftest.txt, exit code 0 = pass.
/// </summary>
static class SelfTest
{
    public static int Run(string file, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new List<string>();
        void Check(bool ok, string what) { log.Add($"{(ok ? "PASS" : "FAIL")} {what}"); if (!ok) failed = true; }
        failed = false;
        try
        {
            using (var deck = Deck.Open(file))
            {
                var slides = deck.Slides;
                for (int i = 0; i < slides.Count; i++)
                {
                    var hits = new List<ShapeHit>();
                    Png(deck.Renderer.Render(slides[i], hits), Path.Combine(outDir, $"slide{i + 1}.png"));
                    log.Add($"slide {i + 1}: {hits.Count} shapes");
                }

                var shape = slides[0].Slide!.Descendants<P.Shape>().First(s => s.TextBody != null);
                deck.Snapshot(slides[0]);
                Deck.SetText(shape, "수정됨\n둘째 줄");
                Deck.EditRuns(shape, r => Deck.SetAttr(r, "sz", "4000"));
                Check(Deck.GetText(shape) == "수정됨\n둘째 줄", "SetText/GetText");

                var ink = new Dictionary<SlidePart, StrokeCollection>
                {
                    [slides[0]] = new StrokeCollection { new Stroke(new StylusPointCollection { new StylusPoint(10, 10), new StylusPoint(300, 200), new StylusPoint(500, 100) }) },
                };
                deck.Save(Path.Combine(outDir, "with-ink.pptx"), ink);
                deck.Save(Path.Combine(outDir, "plain.pptx"), null);
                Check(!deck.Dirty, "save clears dirty");

                deck.Duplicate(0);
                deck.NewSlide(0);
                deck.DeleteSlide(1);
                deck.Save(Path.Combine(outDir, "slides.pptx"), null);
                Check(deck.Undo() != null, "undo available");
            }

            int n = Slides(file);
            using (var d = PresentationDocument.Open(Path.Combine(outDir, "with-ink.pptx"), false))
            {
                var s0 = (SlidePart)d.PresentationPart!.GetPartById(d.PresentationPart.Presentation!.SlideIdList!.Elements<P.SlideId>().First().RelationshipId!);
                Check(s0.Slide!.Descendants<P.Picture>().Any(p => p.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name == "판서"), "ink picture baked in");
                Check(s0.Slide!.InnerText.Contains("수정됨"), "edited text saved (ink version)");
                var edited = s0.Slide!.Descendants<P.Shape>().First(s => s.InnerText.Contains("수정됨"));
                Check(edited.Descendants<A.RunProperties>().All(r => r.FontSize?.Value == 4000), "font size saved");
            }
            using (var d = PresentationDocument.Open(Path.Combine(outDir, "plain.pptx"), false))
            {
                var slide0 = d.PresentationPart!.SlideParts.First(sp => sp.Slide!.InnerText.Contains("수정됨"));
                Check(!slide0.Slide!.Descendants<P.Picture>().Any(p => p.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name == "판서"), "plain version has no ink");
            }
            Check(Slides(Path.Combine(outDir, "slides.pptx")) == n + 1, "duplicate + new - delete = +1 slide");
            using (var deck = Deck.Open(Path.Combine(outDir, "slides.pptx")))
                foreach (var s in deck.Slides) deck.Renderer.Render(s); // reopened file still renders
            Check(true, "reopened file renders");
        }
        catch (Exception ex)
        {
            Check(false, ex.ToString());
        }
        File.WriteAllLines(Path.Combine(outDir, "selftest.txt"), log);
        return failed ? 1 : 0;
    }

    static bool failed;

    static int Slides(string path)
    {
        using var d = PresentationDocument.Open(path, false);
        return d.PresentationPart!.Presentation!.SlideIdList!.Count();
    }

    static void Png(FrameworkElement el, string path)
    {
        el.Measure(new Size(el.Width, el.Height));
        el.Arrange(new Rect(0, 0, el.Width, el.Height));
        el.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)el.Width, (int)el.Height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}
