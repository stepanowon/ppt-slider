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
static partial class SelfTest
{
    public static int Run(string file, string outDir)
    {
        Directory.CreateDirectory(outDir);
        InkStore.Folder = Path.Combine(outDir, "ink"); // never the user's own kept ink
        var log = new List<string>();
        void Check(bool ok, string what) { log.Add($"{(ok ? "PASS" : "FAIL")} {what}"); if (!ok) failed = true; }
        failed = false;
        try { TextEdits(Check); }
        catch (Exception ex) { Check(false, ex.ToString()); }
        try
        {
            using (var deck = Deck.Open(file))
            {
                var slides = deck.Slides;
                for (int i = 0; i < slides.Count; i++)
                {
                    var hits = new List<ShapeHit>();
                    Png(deck.Renderer.Render(slides[i], hits), Path.Combine(outDir, $"slide{i + 1}.png"));
                    // the in-place editor must start from exactly the shape's text, or the edit diff misplaces formatting
                    foreach (var h in hits.Where(h => h.Text != null && Deck.HasText(h.Element)))
                        if (MainWindow.EditText(SlideRenderer.EditDocument(h.Text!)).Text is var t && t != Deck.GetEditText(h.Element))
                            Check(false, $"slide {i + 1}: editor text differs: [{t}] vs [{Deck.GetEditText(h.Element)}]");
                    if (i == 0) EditorBold(hits, Check);
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
        try { ShowTests(file, outDir, Check); }
        catch (Exception ex) { Check(false, ex.ToString()); }
        try { PresenterTests(NotesTest(file, outDir, Check), outDir, Check); }
        catch (Exception ex) { Check(false, ex.ToString()); }
        try { EditorSnaps(file, outDir, Check); }
        catch (Exception ex) { Check(false, ex.ToString()); }
        foreach (var test in new Action<string, string, Action<bool, string>>[] { ZoomTests, BlankPageTests, StraightTests, InkStoreTests, PdfTests })
        {
            try { test(file, outDir, Check); }
            catch (Exception ex) { Check(false, $"{test.Method.Name}: {ex}"); }
        }
        File.WriteAllLines(Path.Combine(outDir, "selftest.txt"), log);
        return failed ? 1 : 0;
    }

    static bool failed;

    /// <summary>Ctrl+B on two characters in the editor of a real shape: exactly those two change weight after the commit.</summary>
    static void EditorBold(List<ShapeHit> hits, Action<bool, string> check)
    {
        foreach (var h in hits.Where(h => h.Text != null && Deck.HasText(h.Element)))
        {
            var doc = SlideRenderer.EditDocument(h.Text!);
            if (doc.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(p => p.Inlines).OfType<System.Windows.Documents.Run>()
                    .FirstOrDefault(r => r.Text.Trim().Length >= 2) is not { } run) continue;
            var before = MainWindow.EditText(doc);
            int skip = run.Text.Length - run.Text.TrimStart().Length;
            MainWindow.Toggle(new System.Windows.Documents.TextRange(run.ContentStart.GetPositionAtOffset(skip), run.ContentStart.GetPositionAtOffset(skip + 2)), "bold");
            var after = MainWindow.EditText(doc);
            var changed = Enumerable.Range(0, after.Fmt.Count).Where(k => after.Fmt[k] != before.Fmt[k]).ToList();
            var shape = h.Element.CloneNode(true);
            Deck.SetEditText(shape, after.Text, after.Fmt, before.Fmt);
            var src = Deck.CharSources(shape);
            check(after.Text == before.Text && changed.Count == 2 && Deck.GetEditText(shape) == before.Text
                  && changed.All(k => X.Attr(src[k]?.GetFirstChild<A.RunProperties>(), "b") == (after.Fmt[k].B ? "1" : "0")),
                $"editor: Ctrl+B on 2 characters of \"{run.Text.Trim()}\" is saved ({changed.Count} changed)");
            return;
        }
    }

    /// <summary>In-place edits (<see cref="Deck.SetEditText"/>) must keep runs, paragraph settings, line breaks and fields.</summary>
    static void TextEdits(Action<bool, string> check)
    {
        static P.Shape Make() => new($$"""
            <p:sp xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
            <p:nvSpPr><p:cNvPr id="2" name="t"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/>
            <a:p><a:pPr algn="ctr"/><a:r><a:rPr b="1" sz="2400"/><a:t>Hello </a:t></a:r><a:r><a:rPr i="1"><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></a:rPr><a:t>World</a:t></a:r><a:br><a:rPr sz="1800"/></a:br><a:r><a:rPr sz="1800"/><a:t>Line2</a:t></a:r><a:endParaRPr sz="2000"/></a:p>
            <a:p><a:pPr lvl="1"/><a:r><a:rPr u="sng"/><a:t>Second</a:t></a:r><a:fld id="{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}" type="slidenum"><a:rPr sz="1000"/><a:t>3</a:t></a:fld></a:p>
            </p:txBody></p:sp>
            """);
        static List<A.Paragraph> Ps(P.Shape s) => s.TextBody!.Elements<A.Paragraph>().ToList();
        // "text[b]|text[i]|BR|FLD:3"
        static string Runs(A.Paragraph p) => string.Join("|", p.ChildElements.Select(e => e switch
        {
            A.Run r => $"{r.Text?.Text}[{(r.RunProperties?.Bold?.Value == true ? "b" : "")}{(r.RunProperties?.Italic?.Value == true ? "i" : "")}{(r.RunProperties?.Underline is { } u && u.Value != A.TextUnderlineValues.None ? "u" : "")}{r.RunProperties?.FontSize?.Value}]",
            A.Field f => $"FLD:{f.Text?.Text}",
            A.Break => "BR",
            _ => null,
        }).OfType<string>());
        static P.Shape Edit(Func<string, string> f) { var s = Make(); Deck.SetEditText(s, f(Deck.GetEditText(s))); return s; }

        var orig = Make();
        check(Deck.GetEditText(orig) == "Hello World\vLine2\nSecond3", "edit: text model");
        check(Edit(t => t).OuterXml == orig.OuterXml, "edit: no change keeps the XML");

        var s = Edit(t => t.Replace("Hello ", "Hello Big "));
        check(Runs(Ps(s)[0]) == "Hello Big [b2400]|World[i]|BR|Line2[1800]", "edit: typed text takes the run before it — " + Runs(Ps(s)[0]));
        check(Ps(s)[1].OuterXml == Ps(orig)[1].OuterXml, "edit: untouched paragraph unchanged");
        check(Ps(s)[0].ParagraphProperties?.Alignment?.InnerText == "ctr" && Ps(s)[0].GetFirstChild<A.Break>()?.RunProperties?.FontSize?.Value == 1800, "edit: pPr and line break kept");

        s = Edit(t => t.Replace("World", "Earth"));
        check(Runs(Ps(s)[0]) == "Hello [b2400]|Earth[i]|BR|Line2[1800]" && s.Descendants<A.RgbColorModelHex>().Any(c => c.Val == "FF0000"),
            "edit: replaced text keeps its formatting — " + Runs(Ps(s)[0]));

        s = Edit(t => t.Replace("Second", "Sec\nond"));
        check(Ps(s).Count == 3 && Runs(Ps(s)[1]) == "Sec[u]" && Runs(Ps(s)[2]) == "ond[u]|FLD:3"
              && Ps(s)[1].ParagraphProperties?.Level?.Value == 1 && Ps(s)[2].ParagraphProperties?.Level?.Value == 1,
            "edit: Enter splits a paragraph, both halves keep its settings — " + string.Join(" / ", Ps(s).Skip(1).Select(Runs)));

        s = Edit(t => t + "!");
        check(Runs(Ps(s)[1]) == "Second[u]|FLD:3|![1000]", "edit: typing after a field keeps the field — " + Runs(Ps(s)[1]));
        s = Edit(t => t.Replace("Second3", "Second4"));
        check(Runs(Ps(s)[1]) == "Second[u]|4[1000]", "edit: an edited field becomes text — " + Runs(Ps(s)[1]));

        s = Edit(t => t.Replace("\vLine2\n", " "));
        check(Ps(s).Count == 1 && Deck.GetEditText(s) == "Hello World Second3" && Ps(s)[0].ParagraphProperties?.Alignment?.InnerText == "ctr",
            "edit: joining paragraphs keeps the first one's settings — " + Runs(Ps(s)[0]));

        s = Edit(t => t.Replace("Line2", "Line2\vMore"));
        check(Runs(Ps(s)[0]) == "Hello [b2400]|World[i]|BR|Line2[1800]|BR|More[1800]", "edit: Shift+Enter adds a line break — " + Runs(Ps(s)[0]));

        s = Edit(t => "X" + t);
        check(Runs(Ps(s)[0]).StartsWith("XHello [b2400]"), "edit: typing at the start takes the first run");

        // bold / italic / underline changed in the editor; Fmt per character as the editor showed it
        var text0 = Deck.GetEditText(orig);
        List<Deck.Fmt> F(string text, Func<int, Deck.Fmt, Deck.Fmt>? change = null) => text.Select((c, k) =>
        {
            int o = k < text0.Length ? k : -1; // only used with same-length edits or explicit changes below
            var f = o < 0 ? default : o < 6 ? new Deck.Fmt(true, false, false) : o < 11 ? new Deck.Fmt(false, true, false) : o >= 18 && o < 24 ? new Deck.Fmt(false, false, true) : default;
            return change?.Invoke(k, f) ?? f;
        }).ToList();
        var fmt0 = F(text0);
        P.Shape Styled(string text, List<Deck.Fmt> fmt) { var sh = Make(); Deck.SetEditText(sh, text, fmt, fmt0); return sh; }

        check(Styled(text0, fmt0).OuterXml == orig.OuterXml, "format: nothing changed keeps the XML");
        s = Styled(text0, F(text0, (k, f) => k is >= 6 and < 11 ? f with { B = true } : f));
        check(Runs(Ps(s)[0]) == "Hello [b2400]|World[bi]|BR|Line2[1800]" && Ps(s)[1].OuterXml == Ps(orig)[1].OuterXml, "format: bold on a whole run — " + Runs(Ps(s)[0]));
        s = Styled(text0, F(text0, (k, f) => k is >= 6 and < 9 ? f with { B = true } : f));
        check(Runs(Ps(s)[0]) == "Hello [b2400]|Wor[bi]|ld[i]|BR|Line2[1800]", "format: bold on part of a run splits it — " + Runs(Ps(s)[0]));
        s = Styled(text0, F(text0, (k, f) => k < 5 ? f with { B = false } : f));
        check(Runs(Ps(s)[0]) == "Hello[2400]| [b2400]|World[i]|BR|Line2[1800]" && Ps(s)[0].Elements<A.Run>().First().RunProperties?.Bold?.Value == false,
            "format: bold off writes b=0 — " + Runs(Ps(s)[0]));
        s = Styled(text0, F(text0, (k, f) => k is >= 18 and < 21 ? f with { U = false, I = true } : f));
        check(Runs(Ps(s)[1]) == "Sec[i]|ond[u]|FLD:3" && Ps(s)[1].Elements<A.Run>().First().RunProperties?.Underline?.InnerText == "none",
            "format: underline off / italic on — " + Runs(Ps(s)[1]));
        var typed = text0.Insert(6, "X");
        s = Styled(typed, F(typed, (k, f) => k == 6 ? new Deck.Fmt(false, false, false) : k > 6 ? F(text0)[k - 1] : f));
        check(Runs(Ps(s)[0]) == "Hello [b2400]|X[2400]|World[i]|BR|Line2[1800]", "format: text typed with bold off — " + Runs(Ps(s)[0]));

        s = Edit(_ => "");
        check(Ps(s).Count == 1 && Ps(s)[0].ParagraphProperties != null && Ps(s)[0].GetFirstChild<A.EndParagraphRunProperties>()?.FontSize?.Value == 2000,
            "edit: deleting everything leaves one styled empty paragraph");
    }

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
