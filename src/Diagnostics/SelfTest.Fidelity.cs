using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace PptSlider;

/// <summary>
/// Rendering rules measured against PowerPoint's own export (PowerPoint COM Slide.Export of real decks):
/// line height, first-paragraph spacing, hanging bullets, Korean wrapping, slide number fields, tints, hairlines, character spacing.
/// </summary>
static partial class SelfTest
{
    static void FidelityTests(string file, string outDir, Action<bool, string> check)
    {
        using var deck = Deck.Open(file);
        var slide = deck.NewSlide(0, blank: true);
        int id = 100;
        void Add(string body, long y, long cy = 1500000, string bodyPr = "<a:bodyPr/>", string spPr = "")
        {
            slide.Slide!.CommonSlideData!.ShapeTree!.Append(new P.Shape($"""
                <p:sp xmlns:p="{NsP}" xmlns:a="{NsA}"><p:nvSpPr><p:cNvPr id="{++id}" name="t{id}"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
                <p:spPr><a:xfrm><a:off x="500000" y="{y}"/><a:ext cx="6000000" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom>{spPr}</p:spPr>
                <p:txBody>{bodyPr}<a:lstStyle/>{body}</p:txBody></p:sp>
                """));
        }
        static string Para(string pPr, string text, string rPr = "sz=\"2000\"") => $"<a:p>{pPr}<a:r><a:rPr lang=\"ko-KR\" {rPr}/><a:t>{text}</a:t></a:r></a:p>";
        const string Spc = "<a:spcBef><a:spcPts val=\"1200\"/></a:spcBef>";
        Add(Para($"<a:pPr>{Spc}</a:pPr>", "첫 문단") + Para($"<a:pPr>{Spc}</a:pPr>", "둘째 문단"), 200000);
        Add(Para("<a:pPr marL=\"342900\" indent=\"-342900\"><a:buChar char=\"•\"/></a:pPr>", "글머리 문단"), 1800000);
        Add(Para("<a:pPr latinLnBrk=\"1\" eaLnBrk=\"1\"/>", "가나 diff를"), 3000000, 500000);
        Add(Para("<a:pPr latinLnBrk=\"0\"/>", "가나다"), 3600000, 500000);
        Add("<a:p><a:r><a:rPr lang=\"ko-KR\"/><a:t>번호 </a:t></a:r><a:fld id=\"{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}\" type=\"slidenum\"><a:rPr lang=\"ko-KR\"/><a:t>‹#›</a:t></a:fld></a:p>", 4200000, 500000);
        Add(Para("", "spacing", "sz=\"2000\" spc=\"-200\""), 4800000, 500000);
        Add("", 5600000, 0, spPr: "<a:noFill/><a:ln w=\"3175\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:ln>");

        int num = deck.Slides.IndexOf(slide) + 1;
        var canvas = deck.Renderer.Render(slide);
        canvas.Measure(new Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        canvas.UpdateLayout();
        var blocks = Descendants(canvas).OfType<TextBlock>().Where(t => t.Parent is TextPanel).ToList();
        TextBlock? Block(string text) => blocks.FirstOrDefault(b => SlideRenderer.Plain(string.Concat(b.Inlines.OfType<Run>().Select(r => r.Text))).Contains(text));
        static string Raw(TextBlock? b) => b == null ? "" : string.Concat(b.Inlines.OfType<Run>().Select(r => r.Text));

        var first = Block("첫 문단"); var second = Block("둘째 문단");
        double px = 20 * 96 / 72.0;
        check(first != null && first.LineStackingStrategy == LineStackingStrategy.BlockLineHeight && Math.Abs(first.LineHeight - px * 1.2) < 0.01,
            $"fidelity: a single line is 1.2 × the font size, like PowerPoint ({first?.LineHeight:0.##} for {px * 1.2:0.##})");
        check(first?.Margin.Top == 0 && Math.Abs((second?.Margin.Top ?? 0) - 16) < 0.01,
            $"fidelity: no space before a box's first paragraph, 12pt before the next ({first?.Margin.Top:0.##}, {second?.Margin.Top:0.##})");

        var bullet = Block("글머리 문단");
        check(bullet != null && Math.Abs(bullet.Margin.Left - 36) < 0.01 && bullet.Inlines.FirstInline is InlineUIContainer { Child: TextBlock { Margin.Left: -36 } },
            $"fidelity: hanging bullet — every line of text starts at marL, the bullet in the indent (left {bullet?.Margin.Left:0.##})");
        var doc = SlideRenderer.EditDocument((FrameworkElement)bullet!.Parent);
        check(TextEditor.EditText(doc).Text == "글머리 문단", "fidelity: the editor's text has no bullet or wrap marks");

        var anyBreak = Raw(Block("가나 diff"));
        check(anyBreak == "가​나 diff⁠를", $"fidelity: latinLnBrk=1 wraps between Korean syllables, never inside \"diff를\" ({Escape(anyBreak)})");
        var noBreak = Raw(Block("가나다"));
        check(noBreak == "가⁠나⁠다", $"fidelity: latinLnBrk=0 keeps a Korean word whole ({Escape(noBreak)})");
        check(SlideRenderer.Plain(Raw(Block("번호"))) == $"번호 {num}", $"fidelity: a slidenum field shows the slide's number ({Raw(Block("번호"))}, slide {num})");
        check(Block("spacing")?.LayoutTransform is ScaleTransform { ScaleX: < 0.95 and > 0.6 } st,
            $"fidelity: negative character spacing narrows the line ({(Block("spacing")?.LayoutTransform as ScaleTransform)?.ScaleX:0.###})");

        var tint = ColorMath.Apply(Color.FromRgb(0x00, 0xA4, 0xF6), [("tint", 0.4)]);
        check(tint == Color.FromRgb(0xCB, 0xE0, 0xFB), $"fidelity: tint 40% of #00A4F6 is PowerPoint's #CBE0FB ({tint})");

        // the zero-height line at y = 5600000 EMU must leave dark pixels
        var bmp = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(canvas);
        int y = (int)Math.Round(5600000 / SlideRenderer.Emu), x = (int)Math.Round(3000000 / SlideRenderer.Emu);
        var px3 = new byte[4 * 3];
        bmp.CopyPixels(new Int32Rect(x, y - 1, 1, 3), px3, 4, 0);
        int darkest = Enumerable.Range(0, 3).Min(i => (int)px3[i * 4]);
        check(darkest < 160, $"fidelity: a horizontal line (zero height, 0.25pt) is drawn at least a pixel wide (darkest {darkest})");
    }

    static string Escape(string s) => s.Replace("​", "|").Replace("⁠", "~");

    static IEnumerable<DependencyObject> Descendants(DependencyObject o)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var c = VisualTreeHelper.GetChild(o, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
