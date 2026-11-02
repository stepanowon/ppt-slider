using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;

namespace PptSlider;

static partial class SelfTest
{
    /// <summary>
    /// A font name in a deck must not make WPF load a font from a path or a share (see <see cref="FontNames"/>): a run's
    /// latin / ea typeface, a list smuggled after a comma, a bullet font and a theme font all point at a folder holding a
    /// real font, and nothing drawn may come from there. Plain names still work, and the font box ignores a path.
    /// </summary>
    static void FontSecurityTests(string file, string outDir, Action<bool, string> check)
    {
        // a folder with a font WPF would happily load, if asked
        var probe = Path.Combine(outDir, "fontprobe");
        Directory.CreateDirectory(probe);
        var arial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        File.Copy(arial, Path.Combine(probe, "probe-font.ttf"), true);
        var local = new Uri(probe + Path.DirectorySeparatorChar).AbsoluteUri + "#Arial";
        string[] evil = [local, "file://127.0.0.1/C$/Windows/Fonts/#Arial", @"\\127.0.0.1\share\#Arial", "Arial, " + local, "./fonts/#Arial", "Arial\n#x"];
        string[] fine = ["Arial", "맑은 고딕", "Segoe UI Semibold", "MS PGothic", "Times New Roman"];

        check(fine.All(FontNames.IsSafe) && !evil.Any(FontNames.IsSafe), "security: plain font names pass, paths / URIs / lists do not");
        var probed = new FontFamily(local).GetTypefaces().Select(t => t.TryGetGlyphTypeface(out var g) ? g.FontUri : null).FirstOrDefault(u => u != null);
        check(probed?.LocalPath.Contains("probe-font", StringComparison.OrdinalIgnoreCase) == true, "security: (control) WPF itself does load a font from a location name");

        using var deck = Deck.Open(file);
        var sp = deck.Slides[0];
        // one box per name: its runs use it, its bullet uses the next one
        for (int i = 0; i < evil.Length; i++)
        {
            var box = ShapeFactory.AddTextBox(sp, new Rect(40, 40 + i * 40, 400, 36), $"상자 {i}");
            foreach (var run in box.Descendants<A.Run>()) TextFormat.SetFont(run.RunProperties ??= new A.RunProperties(), evil[i]);
            var pPr = TextFormat.Paragraphs(box).First().ParagraphProperties ??= new A.ParagraphProperties();
            pPr.RemoveAllChildren();
            pPr.Append(new A.BulletFont { Typeface = evil[(i + 1) % evil.Length] }, new A.CharacterBullet { Char = "•" });
        }
        var plain = ShapeFactory.AddTextBox(sp, new Rect(500, 300, 400, 60), "Plain font");
        TextFormat.EditRuns(plain, r => TextFormat.SetFont(r, "Times New Roman"));

        // and the theme's body font
        var theme = deck.Pres.SlideMasterParts.First().ThemePart!.Theme!;
        var minor = theme.Descendants<A.MinorFont>().First();
        minor.LatinFont!.Typeface = local;
        var themed = ShapeFactory.AddTextBox(sp, new Rect(500, 380, 400, 60), "테마 글꼴");

        var renderer = new SlideRenderer(deck.Pres); // a fresh one reads the changed theme
        var el = renderer.Render(sp);
        el.Measure(new Size(renderer.Width, renderer.Height));
        el.Arrange(new Rect(0, 0, renderer.Width, renderer.Height));
        el.UpdateLayout();
        Png(el, Path.Combine(outDir, "security-fonts.png"));

        var families = new List<FontFamily>();
        void Walk(DependencyObject d)
        {
            if (d is TextBlock tb)
            {
                families.Add(tb.FontFamily);
                families.AddRange(tb.Inlines.SelectMany(Flatten).Select(x => x.FontFamily));
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        static IEnumerable<Inline> Flatten(Inline i) => i is Span s ? s.Inlines.SelectMany(Flatten).Prepend(i) : [i];
        Walk(el);
        var sources = families.Select(f => f.Source).Distinct().ToList();
        check(families.Count > 0 && sources.All(s => !s.Contains('#') && !s.Contains(':') && !s.Contains('/') && !s.Contains('\\')),
            $"security: no font drawn from a deck names a location ({sources.Count} families: {string.Join(" | ", sources)})");
        var loaded = families.SelectMany(f => f.GetTypefaces()).Select(t => t.TryGetGlyphTypeface(out var g) ? g.FontUri.LocalPath : "")
            .Where(p => p != "").Distinct().ToList();
        check(loaded.Count > 0 && !loaded.Any(p => p.Contains("probe-font", StringComparison.OrdinalIgnoreCase) || p.StartsWith(@"\\")),
            $"security: every font drawn comes from the installed fonts ({loaded.Count} files)");
        check(sources.Any(s => s.StartsWith("Times New Roman")), "security: a plain font name in the deck is still used");

        // the font box: a path typed or pasted in is not applied
        var main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, Width = 1400, Height = 860, ShowActivated = false };
        try
        {
            main.Show();
            main.Open(file);
            Wait(100);
            main.InsertCommands.TextBox("글꼴 상자", new Rect(100, 100, 300, 60), edit: false); // selects the new box
            check(main.Selection.Primary != null, "security: a text box to set the font of");
            string? Face() => TextFormat.FirstRunFont(main.Selection.Primary!);
            main.TextCommands.SetFontFace(local);
            check(Face() != local && main.Selection.Primary!.Descendants().All(e => X.Attr(e, "typeface")?.Contains('#') != true), "security: the font box does not apply a path");
            main.TextCommands.SetFontFace("Arial");
            check(Face() == "Arial", "security: the font box still applies a font name");
        }
        finally { main.CloseForTest(); }
    }
}
