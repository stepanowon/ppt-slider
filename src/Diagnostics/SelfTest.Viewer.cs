using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;
using Path = System.IO.Path;

namespace PptSlider;

static partial class SelfTest
{
    const string NsA = "http://schemas.openxmlformats.org/drawingml/2006/main";
    const string NsP = "http://schemas.openxmlformats.org/presentationml/2006/main";
    const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    const string NsDgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    const string NsDsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";

    /// <summary>SmartArt: the saved drawing's shapes are drawn (text in its own box where given); without one, the placeholder box.</summary>
    static void SmartArtTests(string file, string outDir, Action<bool, string> check)
    {
        using var deck = Deck.Open(file);
        static void Write(OpenXmlPart part, string xml)
        {
            using var s = part.GetStream(FileMode.Create);
            s.Write(Encoding.UTF8.GetBytes(xml));
        }
        static string Sp(int id, string geom, long x, long y, long cx, long cy, string? text, string? txXfrm = null)
        {
            var body = text == null ? ""
                : "<dsp:txBody><a:bodyPr anchor=\"ctr\"/><a:lstStyle/><a:p><a:pPr algn=\"ctr\"/><a:r><a:rPr lang=\"ko-KR\" sz=\"2000\"/><a:t>" + text + "</a:t></a:r></a:p></dsp:txBody>";
            return $"""
                <dsp:sp modelId="{id}"><dsp:nvSpPr><dsp:cNvPr id="0" name=""/><dsp:cNvSpPr/></dsp:nvSpPr>
                <dsp:spPr><a:xfrm><a:off x="{x}" y="{y}"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="{geom}"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="2E9E8E"/></a:solidFill></dsp:spPr>
                {body}{txXfrm ?? ""}</dsp:sp>
                """;
        }
        // a slide with a SmartArt frame whose data part points (or not) at a saved drawing
        SlidePart AddSmartArt(bool withDrawing)
        {
            var slide = deck.NewSlide(0, blank: true);
            string? drawId = null;
            if (withDrawing)
            {
                var drawing = slide.AddNewPart<DiagramPersistLayoutPart>();
                Write(drawing, $"""
                    <dsp:drawing xmlns:dgm="{NsDgm}" xmlns:dsp="{NsDsp}" xmlns:a="{NsA}"><dsp:spTree><dsp:nvGrpSpPr><dsp:cNvPr id="0" name=""/><dsp:cNvGrpSpPr/></dsp:nvGrpSpPr><dsp:grpSpPr/>
                    {Sp(1, "chevron", 0, 0, 2000000, 1000000, "단계 1")}
                    {Sp(2, "chevron", 2100000, 0, 2000000, 1000000, "단계 2", """<dsp:txXfrm><a:off x="2300000" y="1100000"/><a:ext cx="1600000" cy="400000"/></dsp:txXfrm>""")}
                    {Sp(3, "ellipse", 4200000, 0, 1000000, 1000000, null)}
                    </dsp:spTree></dsp:drawing>
                    """);
                drawId = slide.GetIdOfPart(drawing);
            }
            var data = slide.AddNewPart<DiagramDataPart>();
            var ext = drawId == null ? ""
                : $"<dgm:extLst><a:ext uri=\"{NsDsp}\"><dsp:dataModelExt xmlns:dsp=\"{NsDsp}\" relId=\"{drawId}\" minVer=\"{NsDgm}\"/></a:ext></dgm:extLst>";
            Write(data, $"""
                <dgm:dataModel xmlns:dgm="{NsDgm}" xmlns:a="{NsA}"><dgm:ptLst/><dgm:cxnLst/><dgm:bg/><dgm:whole/>{ext}</dgm:dataModel>
                """);
            slide.Slide!.CommonSlideData!.ShapeTree!.Append(new P.GraphicFrame($"""
                <p:graphicFrame xmlns:p="{NsP}" xmlns:a="{NsA}" xmlns:r="{NsR}"><p:nvGraphicFramePr><p:cNvPr id="10" name="SmartArt"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr>
                <p:xfrm><a:off x="1000000" y="2000000"/><a:ext cx="6000000" cy="2000000"/></p:xfrm>
                <a:graphic><a:graphicData uri="{NsDgm}"><dgm:relIds xmlns:dgm="{NsDgm}" r:dm="{slide.GetIdOfPart(data)}" r:lo="" r:qs="" r:cs=""/></a:graphicData></a:graphic></p:graphicFrame>
                """));
            return slide;
        }
        static List<FrameworkElement> All(DependencyObject root) =>
            LogicalTreeHelper.GetChildren(root).OfType<FrameworkElement>().SelectMany(e => All(e).Prepend(e)).ToList();
        static string TextOf(FrameworkElement e) => string.Concat(All(e).OfType<TextBlock>().Select(t => t.Text + string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text))));

        static int Paths(FrameworkElement e) => All(e).OfType<System.Windows.Shapes.Path>().Count();
        var smart = AddSmartArt(withDrawing: true);
        var hits = new List<ShapeHit>();
        var canvas = deck.Renderer.Render(smart, hits);
        var frame = smart.Slide!.CommonSlideData!.ShapeTree!.Elements<P.GraphicFrame>().Single();
        frame.Remove();
        int baseline = Paths(deck.Renderer.Render(smart)); // the layout's own shapes
        smart.Slide.CommonSlideData.ShapeTree.Append(frame);
        int paths = Paths(canvas) - baseline;
        check(paths == 3 && !TextOf(canvas).Contains("SmartArt는"), $"smartart: the saved drawing's 3 shapes are drawn ({paths} shapes beside the layout's {baseline})");
        var hosts = canvas.Children.OfType<Grid>().ToList();
        var t1 = hosts.FirstOrDefault(g => TextOf(g).Contains("단계 1"));
        var t2 = hosts.FirstOrDefault(g => TextOf(g).Contains("단계 2"));
        string At(FrameworkElement? g) => g == null ? $"not found among {hosts.Count}: [{string.Join("|", hosts.Select(h => string.Join(",", All(h).Select(e => e.GetType().Name + (e is TextBlock ? "=" + TextOf(e) : "")))))}]" : $"{Canvas.GetLeft(g):0.#},{Canvas.GetTop(g):0.#} {g.Width:0.#}×{g.Height:0.#}";
        check(t1 != null && Math.Abs(Canvas.GetLeft(t1) - 1000000 / SlideRenderer.Emu) < 0.5 && Math.Abs(Canvas.GetTop(t1) - 2000000 / SlideRenderer.Emu) < 0.5,
            $"smartart: shapes are placed relative to the frame ({At(t1)})");
        check(t2 != null && Math.Abs(Canvas.GetTop(t2) - 3100000 / SlideRenderer.Emu) < 0.5 && Math.Abs(t2.Width - 1600000 / SlideRenderer.Emu) < 0.5,
            $"smartart: text with its own box (txXfrm) is drawn in that box ({At(t2)})");
        check(hits.Any(h => h.Element.LocalName == "graphicFrame"), "smartart: the frame is still one selectable object");
        Png(canvas, Path.Combine(outDir, "smartart.png"));

        var bare = AddSmartArt(withDrawing: false);
        check(TextOf(deck.Renderer.Render(bare)).Contains("SmartArt는 표시되지 않습니다"), "smartart: no saved drawing = the placeholder box as before");

        var saved = Path.Combine(outDir, "smartart.pptx");
        deck.Save(saved, null);
        using var reopened = Deck.Open(saved);
        int again = Paths(reopened.Renderer.Render(reopened.Slides[deck.Slides.IndexOf(smart)])) - baseline;
        check(again == 3, $"smartart: still drawn after save and reopen ({again})");
    }

    /// <summary>Recent files: newest first, 10 at most, no duplicates; shown on the empty screen.</summary>
    static void RecentTests(string file, string outDir, Action<bool, string> check)
    {
        var fake = Enumerable.Range(1, 12).Select(i => Path.Combine(outDir, $"recent{i}.pptx")).ToList();
        foreach (var f in fake) RecentFiles.Add(f);
        var list = RecentFiles.Load();
        check(list.Count == 10 && list[0] == fake[11] && list[9] == fake[2], "recent: newest first, 10 at most");
        RecentFiles.Add(fake[5].ToUpperInvariant());
        list = RecentFiles.Load();
        check(list.Count == 10 && list[0].Equals(fake[5], StringComparison.OrdinalIgnoreCase) && list.Count(p => p.Equals(fake[5], StringComparison.OrdinalIgnoreCase)) == 1,
            "recent: opening one again moves it to the top, once");

        var a = Path.Combine(outDir, "recent-a.pptx");
        var b = Path.Combine(outDir, "recent-b.pptx");
        File.Copy(file, a, true);
        File.Copy(file, b, true);
        RecentFiles.Add(a);
        RecentFiles.Add(b);
        var main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, Width = 1200, Height = 760, ShowActivated = false };
        try
        {
            main.Show();
            Wait(150);
            var shown = main.RecentList.Items.OfType<Button>().Select(x => (string)x.Tag).ToList();
            check(shown.Count == 2 && shown[0] == b && shown[1] == a && main.RecentBtn.IsEnabled, "recent: the empty screen lists the files that exist, newest first");
            Snap(main, Path.Combine(outDir, "editor-empty-recent.png"));
            main.RecentList.Items.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(main.Deck?.Path == a && RecentFiles.Load()[0] == a && !main.EmptyPanel.IsVisible, "recent: clicking one opens it and moves it to the top");
        }
        finally { main.CloseForTest(); }
    }

    /// <summary>Unsaved edits are copied aside; after a crash the copy is offered when the file is opened again.</summary>
    static void AutoSaveTests(string file, string outDir, Action<bool, string> check)
    {
        var path = Path.Combine(outDir, "autosave.pptx");
        File.Copy(file, path, true);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
        var copy = AutoSave.FileFor(path);
        P.Shape FirstText(Deck d) => d.Slides[0].Slide!.Descendants<P.Shape>().First(s => s.TextBody != null);

        var main = new MainWindow();
        try
        {
            main.Open(path);
            main.AutoSaveNow();
            check(!File.Exists(copy), "autosave: nothing to copy without edits");
            main.Deck!.Snapshot(main.Deck.Slides[0]);
            Deck.SetText(FirstText(main.Deck), "자동 저장 테스트");
            main.AutoSaveNow();
            check(File.Exists(copy) && AutoSave.Recoverable(path) == copy, "autosave: edits are copied aside, the copy is newer than the file");
        }
        finally { main.CloseForTest(); } // like a crash: no normal close, the copy stays

        bool asked = false;
        main = new MainWindow { AskRecover = _ => asked = true };
        try
        {
            main.Open(path);
            var d = main.Deck!;
            check(asked && d.Dirty && d.Path == path && Deck.GetText(FirstText(d)) == "자동 저장 테스트" && main.Title.Contains('*'),
                "autosave: reopening offers the copy; yes = the edits are back, unsaved, under the file's own name");
            main.Run("save");
            check(!File.Exists(copy) && !d.Dirty, "autosave: saving removes the copy");
        }
        finally { main.CloseForTest(); }
        using (var d = Deck.Open(path))
            check(Deck.GetText(FirstText(d)) == "자동 저장 테스트", "autosave: the recovered edits were saved into the file");

        main = new MainWindow();
        try
        {
            main.Open(path);
            main.Deck!.Snapshot(main.Deck.Slides[0]);
            Deck.SetText(FirstText(main.Deck), "버릴 편집");
            main.AutoSaveNow();
        }
        finally { main.CloseForTest(); }
        asked = false;
        main = new MainWindow { AskRecover = _ => { asked = true; return false; } };
        try
        {
            main.Open(path);
            check(asked && !main.Deck!.Dirty && Deck.GetText(FirstText(main.Deck)) == "자동 저장 테스트" && !File.Exists(copy),
                "autosave: no = the file as it is, and the copy is removed");
        }
        finally { main.CloseForTest(); }

        File.Copy(path, copy, true);
        File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddHours(-1));
        check(AutoSave.Recoverable(path) == null, "autosave: a copy older than the file is not offered");
        AutoSave.Delete(path);
    }
}
