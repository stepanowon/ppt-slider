using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Path = System.Windows.Shapes.Path;

namespace PptSlider;

/// <summary>A top-level shape on the slide, where it was drawn (slide DIPs) and its drawn text, if any.</summary>
public record ShapeHit(OpenXmlElement Element, Rect Bounds, FrameworkElement? Text = null);

/// <summary>
/// Draws PPTX slides as WPF visuals: backgrounds, master/layout graphics, shapes, text, pictures, groups, tables.
/// SmartArt is drawn from the shapes PowerPoint saved with it (drawing.xml).
/// ponytail: charts, effects, table styles and animations are not rendered; add when decks need them.
/// </summary>
public class SlideRenderer
{
    public const double Emu = 9525; // EMU per WPF DIP (96 dpi)
    readonly PresentationPart pres;
    readonly Dictionary<Uri, ImageSource?> images = new();
    readonly Dictionary<SlideMasterPart, Theme> themes = new();
    public double Width { get; }
    public double Height { get; }
    FrameworkElement? lastText; // text panel of the shape DrawShape just drew

    public SlideRenderer(PresentationPart pres)
    {
        this.pres = pres;
        var sz = X.Child(pres.Presentation, "sldSz");
        Width = (X.Long(sz, "cx") ?? 12192000) / Emu;
        Height = (X.Long(sz, "cy") ?? 6858000) / Emu;
    }

    record Theme(Dictionary<string, Color> Colors, Dictionary<string, string> Fonts, List<double> LineWidths);

    record Ctx(OpenXmlPart Part, Theme T, Dictionary<string, string> ClrMap,
        OpenXmlElement? LayoutTree, OpenXmlElement? MasterTree, OpenXmlElement? TxStyles, OpenXmlElement? DefaultText);

    readonly record struct Map(double Ox, double Oy, double Sx, double Sy)
    {
        public Rect R(OpenXmlElement xfrm)
        {
            var off = X.Child(xfrm, "off"); var ext = X.Child(xfrm, "ext");
            return new Rect(Ox + (X.Long(off, "x") ?? 0) * Sx, Oy + (X.Long(off, "y") ?? 0) * Sy,
                Math.Max(0, (X.Long(ext, "cx") ?? 0) * Sx), Math.Max(0, (X.Long(ext, "cy") ?? 0) * Sy));
        }
    }

    public Canvas Render(SlidePart slide, List<ShapeHit>? hits = null)
    {
        var layout = slide.SlideLayoutPart!;
        var master = layout.SlideMasterPart!;
        var theme = ThemeOf(master);
        var clrMap = X.Child(master.SlideMaster, "clrMap")?.GetAttributes().ToDictionary(a => a.LocalName, a => a.Value ?? "")
                     ?? new Dictionary<string, string>();
        var txStyles = X.Child(master.SlideMaster, "txStyles");
        var defText = X.Child(pres.Presentation, "defaultTextStyle");
        OpenXmlElement? Tree(OpenXmlElement? root) => X.Child(X.Child(root, "cSld"), "spTree");

        var slideCtx = new Ctx(slide, theme, clrMap, Tree(layout.SlideLayout), Tree(master.SlideMaster), txStyles, defText);
        var layoutCtx = slideCtx with { Part = layout, LayoutTree = null };
        var masterCtx = slideCtx with { Part = master, LayoutTree = null, MasterTree = null };

        var canvas = new Canvas { Width = Width, Height = Height, ClipToBounds = true, Background = Brushes.White };
        foreach (var (root, c) in new (OpenXmlElement?, Ctx)[] { (slide.Slide, slideCtx), (layout.SlideLayout, layoutCtx), (master.SlideMaster, masterCtx) })
        {
            var bg = X.Child(X.Child(root, "cSld"), "bg");
            if (bg == null) continue;
            var bgPr = X.Child(bg, "bgPr");
            canvas.Background = bgPr != null ? Fill(bgPr, c, out _) ?? Brushes.White : Solid(ColorOf(X.Child(bg, "bgRef"), c)) ?? Brushes.White;
            break;
        }

        var root0 = new Map(0, 0, 1 / Emu, 1 / Emu);
        bool showMaster = X.Attr(slide.Slide, "showMasterSp") != "0";
        if (showMaster && X.Attr(layout.SlideLayout, "showMasterSp") != "0") DrawTree(Tree(master.SlideMaster), masterCtx, root0, canvas, true, null);
        if (showMaster) DrawTree(Tree(layout.SlideLayout), layoutCtx, root0, canvas, true, null);
        DrawTree(Tree(slide.Slide), slideCtx, root0, canvas, false, hits);
        return canvas;
    }

    /// <param name="smartArt">A SmartArt drawing (dsp:spTree): shapes may put their text in a box of its own.</param>
    void DrawTree(OpenXmlElement? tree, Ctx c, Map m, Panel target, bool skipPh, List<ShapeHit>? hits, bool smartArt = false)
    {
        if (tree == null) return;
        foreach (var child in tree.ChildElements)
        {
            var el = child;
            if (el.LocalName == "AlternateContent")
                el = X.Child(el, "Fallback")?.FirstChild ?? X.Child(el, "Choice")?.FirstChild;
            if (el == null || (skipPh && Ph(el) != null)) continue;
            Rect? r = null;
            lastText = null;
            try
            {
                r = el.LocalName switch
                {
                    "sp" when smartArt => SplitText(el).Select(part => DrawShape(part, c, m, target)).ToList()[0],
                    "sp" or "cxnSp" or "pic" => DrawShape(el, c, m, target),
                    "grpSp" => DrawGroup(el, c, m, target),
                    "graphicFrame" => DrawFrame(el, c, m, target),
                    _ => null,
                };
            }
            catch (Exception) { /* one malformed shape must not kill the whole slide */ }
            if (r != null) hits?.Add(new ShapeHit(child, r.Value, el.LocalName == "sp" ? lastText : null));
        }
    }

    /// <summary>
    /// SmartArt finds its saved drawing through the data part: dgm:relIds r:dm → the data model's dsp:dataModelExt relId
    /// → the slide's DiagramPersistLayoutPart. Null when the file has no saved drawing (it is then a placeholder box).
    /// </summary>
    static DiagramPersistLayoutPart? SmartArtDrawing(OpenXmlElement? graphicData, Ctx c)
    {
        var relIds = X.Child(graphicData, "relIds");
        if (relIds == null) return null;
        var dm = relIds.GetAttributes().FirstOrDefault(a => a.LocalName == "dm").Value;
        if (dm == null || !c.Part.TryGetPartById(dm, out var data)) return null;
        var ext = data.RootElement?.Descendants().FirstOrDefault(e => e.LocalName == "dataModelExt");
        return X.Attr(ext, "relId") is { } id && c.Part.TryGetPartById(id, out var part) ? part as DiagramPersistLayoutPart : null;
    }

    const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>A SmartArt shape whose text has its own box (dsp:txXfrm) is drawn as the shape without its text, then the text alone in that box.</summary>
    static OpenXmlElement[] SplitText(OpenXmlElement sp)
    {
        if (X.Child(sp, "txXfrm") is not { } box || X.Child(sp, "txBody") == null) return [sp];
        var shape = sp.CloneNode(true);
        X.Child(shape, "txBody")!.Remove();
        var text = sp.CloneNode(true);
        X.Child(text, "style")?.Remove();
        var spPr = X.Child(text, "spPr")!;
        spPr.RemoveAllChildren();
        var xfrm = new OpenXmlUnknownElement("a", "xfrm", DrawingNs);
        foreach (var a in box.GetAttributes()) xfrm.SetAttribute(a);
        foreach (var e in box.ChildElements) xfrm.AppendChild(e.CloneNode(true));
        var ln = new OpenXmlUnknownElement("a", "ln", DrawingNs);
        ln.AppendChild(new OpenXmlUnknownElement("a", "noFill", DrawingNs));
        spPr.Append(xfrm, new OpenXmlUnknownElement("a", "noFill", DrawingNs), ln);
        return [shape, text];
    }

    // ---------- shapes ----------

    static OpenXmlElement? Ph(OpenXmlElement el) =>
        X.Child(X.Child(el.ChildElements.FirstOrDefault(e => e.LocalName.StartsWith("nv")), "nvPr"), "ph");

    static string NormPh(string? t) => t switch { null or "obj" or "subTitle" => "body", "ctrTitle" => "title", _ => t };

    static OpenXmlElement? FindPh(OpenXmlElement? tree, string? type, string? idx)
    {
        if (tree == null) return null;
        var phs = tree.Descendants().Where(e => e.LocalName == "ph").ToList();
        if (idx != null && phs.FirstOrDefault(p => X.Attr(p, "idx") == idx) is { } byIdx) return byIdx.Parent?.Parent?.Parent;
        return phs.FirstOrDefault(p => NormPh(X.Attr(p, "type")) == NormPh(type))?.Parent?.Parent?.Parent;
    }

    /// <summary>Layout and master shapes a placeholder inherits from, plus the effective placeholder type.</summary>
    static (List<OpenXmlElement> chain, string? type) PhChain(OpenXmlElement el, Ctx c)
    {
        var ph = Ph(el);
        var chain = new List<OpenXmlElement>();
        if (ph == null) return (chain, null);
        var type = X.Attr(ph, "type");
        if (FindPh(c.LayoutTree, type, X.Attr(ph, "idx")) is { } l)
        {
            chain.Add(l);
            type ??= X.Attr(Ph(l), "type");
        }
        if (FindPh(c.MasterTree, type, null) is { } mst) chain.Add(mst);
        return (chain, type ?? "body");
    }

    Rect? DrawShape(OpenXmlElement el, Ctx c, Map m, Panel target)
    {
        var (chain, phType) = PhChain(el, c);
        var all = chain.Prepend(el).ToList();
        var xfrm = all.Select(s => X.Child(X.Child(s, "spPr"), "xfrm")).FirstOrDefault(x => x != null);
        if (xfrm == null) return null;
        var r = m.R(xfrm);
        var spPr = X.Child(el, "spPr");
        var style = X.Child(el, "style");

        var host = new Grid { Width = r.Width, Height = r.Height };
        Canvas.SetLeft(host, r.X); Canvas.SetTop(host, r.Y);
        if (X.Long(xfrm, "rot") is { } rot && rot != 0)
        {
            host.RenderTransformOrigin = new Point(0.5, 0.5);
            host.RenderTransform = new RotateTransform(rot / 60000.0);
        }

        Brush? fill = null;
        if (X.Child(el, "blipFill") is { } blip) fill = BlipBrush(blip, c);
        else
        {
            bool decided = false;
            foreach (var s in all) { fill = Fill(X.Child(s, "spPr"), c, out decided); if (decided) break; }
            if (!decided) fill = Solid(ColorOf(X.Child(style, "fillRef"), c), X.Attr(X.Child(style, "fillRef"), "idx") is null or "0");
        }
        var (stroke, sw, dash) = Line(spPr, style, c);
        if (fill != null || stroke != null)
        {
            var geom = Geometry(spPr, r.Width, r.Height, (X.Long(X.Child(xfrm, "ext"), "cx") ?? 1), (X.Long(X.Child(xfrm, "ext"), "cy") ?? 1));
            var path = new Path { Data = geom, Fill = fill, Stroke = stroke, StrokeThickness = sw, StrokeDashArray = dash };
            bool fh = X.Attr(xfrm, "flipH") == "1", fv = X.Attr(xfrm, "flipV") == "1";
            if (fh || fv) { path.RenderTransformOrigin = new Point(0.5, 0.5); path.RenderTransform = new ScaleTransform(fh ? -1 : 1, fv ? -1 : 1); }
            host.Children.Add(path);
        }

        if (X.Child(el, "txBody") is { } tx)
        {
            var lists = new List<OpenXmlElement?> { X.Child(tx, "lstStyle") };
            lists.AddRange(chain.Select(s => X.Child(X.Child(s, "txBody"), "lstStyle")));
            if (phType != null)
                lists.Add(X.Child(c.TxStyles, NormPh(phType) switch { "title" => "titleStyle", "body" => "bodyStyle", _ => "otherStyle" }));
            else { lists.Add(c.DefaultText); lists.Add(X.Child(c.TxStyles, "otherStyle")); }
            var bodies = all.Select(s => X.Child(X.Child(s, "txBody"), "bodyPr")).ToList();
            var fontRef = ColorOf(X.Child(style, "fontRef"), c);
            if (Text(tx, lists, bodies, c, fontRef) is { } text)
            {
                // "shrink text on overflow": the first bodyPr that says how to fit decides
                var fit = bodies.Select(b => b?.ChildElements.FirstOrDefault(e => e.LocalName is "normAutofit" or "spAutoFit" or "noAutofit")).FirstOrDefault(e => e != null);
                if (fit?.LocalName == "normAutofit") text = Fit(text, new Size(r.Width, r.Height), s => Text(tx, lists, bodies, c, fontRef, s)!);
                host.Children.Add(lastText = text);
            }
        }
        target.Children.Add(host);
        return r;
    }

    Rect? DrawGroup(OpenXmlElement el, Ctx c, Map m, Panel target)
    {
        var x = X.Child(X.Child(el, "grpSpPr"), "xfrm");
        if (x == null) { DrawTree(el, c, m, target, false, null); return null; }
        var r = m.R(x);
        var off = X.Child(x, "off"); var ext = X.Child(x, "ext"); var chOff = X.Child(x, "chOff"); var chExt = X.Child(x, "chExt");
        double kx = (X.Long(ext, "cx") ?? 1) / (double)Math.Max(1, X.Long(chExt, "cx") ?? X.Long(ext, "cx") ?? 1);
        double ky = (X.Long(ext, "cy") ?? 1) / (double)Math.Max(1, X.Long(chExt, "cy") ?? X.Long(ext, "cy") ?? 1);
        var inner = new Map(
            m.Ox + m.Sx * ((X.Long(off, "x") ?? 0) - (X.Long(chOff, "x") ?? 0) * kx),
            m.Oy + m.Sy * ((X.Long(off, "y") ?? 0) - (X.Long(chOff, "y") ?? 0) * ky),
            m.Sx * kx, m.Sy * ky);
        DrawTree(el, c, inner, target, false, null);
        return r;
    }

    Rect? DrawFrame(OpenXmlElement el, Ctx c, Map m, Panel target)
    {
        var x = X.Child(el, "xfrm");
        if (x == null) return null;
        var r = m.R(x);
        var data = X.Child(X.Child(el, "graphic"), "graphicData");
        if (SmartArtDrawing(data, c) is { } drawing)
        {
            // the shapes PowerPoint laid out and saved (drawing.xml), positioned relative to the frame
            var off = X.Child(x, "off");
            var inner = m with { Ox = m.Ox + (X.Long(off, "x") ?? 0) * m.Sx, Oy = m.Oy + (X.Long(off, "y") ?? 0) * m.Sy };
            DrawTree(X.Child(drawing.RootElement, "spTree"), c with { Part = drawing }, inner, target, false, null, smartArt: true);
            return r;
        }
        FrameworkElement ui;
        if (X.Child(data, "tbl") is { } tbl) ui = Table(tbl, c, m, r);
        else if (data?.Descendants().FirstOrDefault(d => d.LocalName == "blipFill") is { } blip)
            ui = new Border { Background = BlipBrush(blip, c) }; // OLE objects carry a preview image
        else
            ui = new Border
            {
                BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = L.T("notShown"), Foreground = Brushes.Gray, FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        ui.Width = r.Width;
        if (ui is Border) ui.Height = r.Height;
        Canvas.SetLeft(ui, r.X); Canvas.SetTop(ui, r.Y);
        target.Children.Add(ui);
        return r;
    }

    FrameworkElement Table(OpenXmlElement tbl, Ctx c, Map m, Rect r)
    {
        var grid = new Grid();
        foreach (var col in X.Child(tbl, "tblGrid")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((X.Long(col, "w") ?? 0) * m.Sx) });
        var lists = new List<OpenXmlElement?> { c.DefaultText, X.Child(c.TxStyles, "otherStyle") };
        // PowerPoint's default table style "Medium Style 2 - Accent 1"; ponytail: other table styles render unstyled
        var tblPr = X.Child(tbl, "tblPr");
        var styleId = X.Child(tblPr, "tableStyleId")?.InnerText;
        bool medium2 = styleId is null or "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}";
        bool firstRow = X.Attr(tblPr, "firstRow") is "1" or "true", band = X.Attr(tblPr, "bandRow") is "1" or "true";
        var accent = Scheme("accent1", c);
        Color Tint(double t) => ColorMath.Apply(accent, [("tint", t)]);
        int ri = 0;
        foreach (var tr in tbl.ChildElements.Where(e => e.LocalName == "tr"))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            double h = (X.Long(tr, "h") ?? 0) * m.Sy;
            bool header = medium2 && firstRow && ri == 0;
            int dataRow = ri - (firstRow ? 1 : 0);
            Brush? styleFill = !medium2 ? null : new SolidColorBrush(header ? accent : band && dataRow % 2 == 0 ? Tint(0.4) : Tint(0.2));
            int ci = 0;
            foreach (var tc in tr.ChildElements.Where(e => e.LocalName == "tc"))
            {
                if (X.Attr(tc, "hMerge") == "1" || X.Attr(tc, "vMerge") == "1") { ci++; continue; }
                var tcPr = X.Child(tc, "tcPr");
                var fill = Fill(tcPr, c, out var hasFill);
                var cell = new Border
                {
                    MinHeight = h,
                    BorderBrush = medium2 ? Brushes.White : Brushes.Gray, BorderThickness = new Thickness(medium2 ? 1 : 0.75),
                    Background = hasFill ? fill : styleFill,
                    Child = Text(X.Child(tc, "txBody"), lists, new List<OpenXmlElement?> { tcPr }, c, header ? Colors.White : null),
                };
                if (header && cell.Child is Panel p) foreach (var t in p.Children.OfType<TextBlock>()) t.FontWeight = FontWeights.Bold;
                Grid.SetRow(cell, ri); Grid.SetColumn(cell, ci);
                Grid.SetColumnSpan(cell, (int)(X.Long(tc, "gridSpan") ?? 1));
                Grid.SetRowSpan(cell, (int)(X.Long(tc, "rowSpan") ?? 1));
                grid.Children.Add(cell);
                ci++;
            }
            ri++;
        }
        return grid;
    }

    // ---------- text ----------

    /// <summary>
    /// The text at the largest size (down to a quarter) that fits <paramref name="box"/>, like PowerPoint's "shrink text on overflow".
    /// The file's own fontScale is where it starts; <paramref name="build"/> makes the text at a further scale.
    /// </summary>
    static FrameworkElement Fit(FrameworkElement text, Size box, Func<double, FrameworkElement> build)
    {
        bool Fits(FrameworkElement t)
        {
            t.Measure(new Size(box.Width, double.PositiveInfinity));
            return t.DesiredSize.Height <= box.Height + 0.5;
        }
        if (Fits(text)) return text;
        double lo = 0.25, hi = 1;
        var best = build(lo);
        for (int i = 0; i < 7; i++)
        {
            double mid = (lo + hi) / 2;
            var t = build(mid);
            if (Fits(t)) { lo = mid; best = t; } else hi = mid;
        }
        return best;
    }

    /// <summary>The text of a shape or cell; the panel's Tag is the font scale it was drawn at (see <see cref="Fit"/>).</summary>
    FrameworkElement? Text(OpenXmlElement? tx, List<OpenXmlElement?> lists, List<OpenXmlElement?> bodies, Ctx c, Color? fontRef, double shrink = 1)
    {
        if (tx == null) return null;
        string? B(string n) => bodies.Select(b => X.Attr(b, n)).FirstOrDefault(v => v != null);
        // bodyPr uses lIns/tIns/..., table cells (tcPr) use marL/marT/...
        double Ins(string n, long d) => (long.TryParse(B(n) ?? B("mar" + char.ToUpper(n[0])), out var v) ? v : d) / Emu;
        double fontScale = (X.Long(X.Child(X.Child(tx, "bodyPr"), "normAutofit"), "fontScale") ?? 100000) / 100000.0 * shrink;
        bool wrap = B("wrap") != "none";

        var panel = new StackPanel
        {
            Tag = fontScale,
            Margin = new Thickness(Ins("lIns", 91440), Ins("tIns", 45720), Ins("rIns", 91440), Ins("bIns", 45720)),
            VerticalAlignment = B("anchor") switch { "ctr" => VerticalAlignment.Center, "b" => VerticalAlignment.Bottom, _ => VerticalAlignment.Top },
        };
        int autoNum = 0;
        foreach (var p in tx.ChildElements.Where(e => e.LocalName == "p"))
        {
            var pPr = X.Child(p, "pPr");
            int lvl = (int)(X.Long(pPr, "lvl") ?? 0);
            var levels = new List<OpenXmlElement> { };
            if (pPr != null) levels.Add(pPr);
            levels.AddRange(lists.Select(l => X.Child(l, $"lvl{lvl + 1}pPr")).OfType<OpenXmlElement>());
            string? P(string n) => levels.Select(l => X.Attr(l, n)).FirstOrDefault(v => v != null);
            OpenXmlElement? PC(string n) => levels.Select(l => X.Child(l, n)).FirstOrDefault(v => v != null);
            var defs = levels.Select(l => X.Child(l, "defRPr")).OfType<OpenXmlElement>().ToList();

            string? R(OpenXmlElement? rPr, string n) => X.Attr(rPr, n) ?? defs.Select(d => X.Attr(d, n)).FirstOrDefault(v => v != null);
            double Size(OpenXmlElement? rPr) => (long.TryParse(R(rPr, "sz"), out var s) ? s : 1800) / 100.0 * fontScale * 96 / 72;
            Brush Fore(OpenXmlElement? rPr)
            {
                var col = ColorOf(X.Child(rPr, "solidFill"), c) ?? fontRef
                          ?? defs.Select(d => ColorOf(X.Child(d, "solidFill"), c)).FirstOrDefault(v => v != null)
                          ?? ColorOf(null, c, schemeFallback: "tx1");
                return new SolidColorBrush(col ?? Colors.Black);
            }
            FontFamily Font(OpenXmlElement? rPr)
            {
                string Face(string n) => X.Attr(X.Child(rPr, n), "typeface") ?? defs.Select(d => X.Attr(X.Child(d, n), "typeface")).FirstOrDefault(v => v != null) ?? (n == "latin" ? "+mn-lt" : "+mn-ea");
                string Res(string f) => f.StartsWith('+') ? c.T.Fonts.GetValueOrDefault(f, "") : f;
                return new FontFamily(string.Join(", ", new[] { Res(Face("latin")), Res(Face("ea")), "Malgun Gothic" }.Where(f => f != "")));
            }

            var runs = p.ChildElements.Where(e => e.LocalName is "r" or "br" or "fld").ToList();
            var endPr = X.Child(p, "endParaRPr");
            var firstRPr = runs.Select(r => X.Child(r, "rPr")).FirstOrDefault(r => r != null) ?? endPr;
            double size = Size(firstRPr);
            var tb = new TextBlock
            {
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextAlignment = P("algn") switch { "ctr" => TextAlignment.Center, "r" => TextAlignment.Right, "just" or "dist" => TextAlignment.Justify, _ => TextAlignment.Left },
                FontSize = size, FontFamily = Font(firstRPr), Foreground = Fore(firstRPr),
            };
            double Spc(string n)
            {
                var s = PC(n);
                if (X.Child(s, "spcPts") is { } pts) return (X.Long(pts, "val") ?? 0) / 100.0 * 96 / 72;
                if (X.Child(s, "spcPct") is { } pct) return size * 1.2 * (X.Long(pct, "val") ?? 0) / 100000.0;
                return 0;
            }
            double marL = (long.TryParse(P("marL"), out var ml) ? ml : 0) / Emu, indent = (long.TryParse(P("indent"), out var ind) ? ind : 0) / Emu;
            tb.Margin = new Thickness(Math.Max(0, marL + indent), Spc("spcBef"), 0, Spc("spcAft"));
            if (X.Child(PC("lnSpc"), "spcPct") is { } lnPct)
            {
                tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                tb.LineHeight = Math.Max(1, size * 1.2 * (X.Long(lnPct, "val") ?? 100000) / 100000.0);
            }

            bool hasText = runs.Any(r => r.LocalName != "br" && !string.IsNullOrEmpty(X.Child(r, "t")?.InnerText));
            var bulletDef = levels.Select(l => l.ChildElements.FirstOrDefault(ch => ch.LocalName is "buNone" or "buChar" or "buAutoNum")).FirstOrDefault(v => v != null);
            if (bulletDef?.LocalName != "buAutoNum") autoNum = 0;
            if (hasText && bulletDef is { LocalName: "buChar" or "buAutoNum" })
            {
                var bu = new Run(bulletDef.LocalName == "buChar" ? X.Attr(bulletDef, "char") ?? "•" : $"{++autoNum}.")
                {
                    Foreground = Solid(ColorOf(PC("buClr"), c)) ?? Fore(firstRPr),
                    FontSize = size * (X.Long(PC("buSzPct"), "val") ?? 100000) / 100000.0,
                };
                if (X.Attr(PC("buFont"), "typeface") is { } bf && !bf.StartsWith('+')) bu.FontFamily = new FontFamily(bf);
                bu.Tag = Bullet;
                tb.Inlines.Add(bu);
                tb.Inlines.Add(new Run("  ") { FontSize = size * 0.6, Tag = Bullet });
            }
            foreach (var r in runs)
            {
                if (r.LocalName == "br") { tb.Inlines.Add(new LineBreak()); continue; }
                var rPr = X.Child(r, "rPr");
                var run = new Run(X.Child(r, "t")?.InnerText ?? "")
                {
                    FontSize = Size(rPr), FontFamily = Font(rPr), Foreground = Fore(rPr),
                };
                // only set when on, so a table header can still make the whole cell bold
                if (R(rPr, "b") is "1" or "true") run.FontWeight = FontWeights.Bold;
                if (R(rPr, "i") is "1" or "true") run.FontStyle = FontStyles.Italic;
                if (R(rPr, "u") is { } u && u != "none") run.TextDecorations = TextDecorations.Underline;
                if (R(rPr, "strike") is "sngStrike" or "dblStrike") run.TextDecorations = TextDecorations.Strikethrough;
                tb.Inlines.Add(run);
            }
            panel.Children.Add(tb);
        }
        return panel;
    }

    static readonly object Bullet = new();

    /// <summary>
    /// The text a <see cref="Text"/> panel drew, as an editable document that looks the same.
    /// Bullets become non-text inline objects, so the document's text matches <see cref="Deck.GetEditText"/>.
    /// </summary>
    public static FlowDocument EditDocument(FrameworkElement textPanel)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0) };
        foreach (var tb in ((Panel)textPanel).Children.OfType<TextBlock>())
        {
            var p = new Paragraph
            {
                Margin = tb.Margin, TextAlignment = tb.TextAlignment, LineHeight = tb.LineHeight, LineStackingStrategy = tb.LineStackingStrategy,
                FontSize = tb.FontSize, FontFamily = tb.FontFamily, Foreground = tb.Foreground,
            };
            TextBlock? bullet = null;
            foreach (var inl in tb.Inlines)
            {
                if (inl is LineBreak) { p.Inlines.Add(new LineBreak()); continue; }
                if (inl is not Run r) continue;
                var copy = new Run(r.Text)
                {
                    FontSize = r.FontSize, FontFamily = r.FontFamily, Foreground = r.Foreground,
                    FontWeight = r.FontWeight, FontStyle = r.FontStyle, TextDecorations = r.TextDecorations,
                };
                if (r.Tag != Bullet) { p.Inlines.Add(copy); continue; }
                if (bullet == null) p.Inlines.Add(new InlineUIContainer(bullet = new TextBlock()) { BaselineAlignment = BaselineAlignment.Baseline });
                bullet.Inlines.Add(copy);
            }
            doc.Blocks.Add(p);
        }
        return doc;
    }

    // ---------- fills, lines, colors ----------

    Brush? Fill(OpenXmlElement? container, Ctx c, out bool decided)
    {
        decided = true;
        foreach (var f in container?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            switch (f.LocalName)
            {
                case "noFill": return null;
                case "solidFill": return Solid(ColorOf(f, c));
                case "pattFill": return Solid(ColorOf(X.Child(f, "fgClr"), c));
                case "blipFill": return BlipBrush(f, c);
                case "grpFill": return null; // ponytail: group fill inheritance not tracked
                case "gradFill":
                    var stops = new GradientStopCollection(X.Child(f, "gsLst")?.ChildElements
                        .Select(gs => new GradientStop(ColorOf(gs, c) ?? Colors.Gray, (X.Long(gs, "pos") ?? 0) / 100000.0)) ?? []);
                    if (X.Child(f, "path") != null) return new RadialGradientBrush(stops);
                    double a = (X.Long(X.Child(f, "lin"), "ang") ?? 0) / 60000.0 * Math.PI / 180;
                    return new LinearGradientBrush(stops, new Point(0.5 - Math.Cos(a) / 2, 0.5 - Math.Sin(a) / 2), new Point(0.5 + Math.Cos(a) / 2, 0.5 + Math.Sin(a) / 2));
            }
        }
        decided = false;
        return null;
    }

    (Brush?, double, DoubleCollection?) Line(OpenXmlElement? spPr, OpenXmlElement? style, Ctx c)
    {
        var ln = X.Child(spPr, "ln");
        var lnRef = X.Child(style, "lnRef");
        int idx = (int)(X.Long(lnRef, "idx") ?? 0);
        double w = (X.Long(ln, "w") ?? (idx > 0 && idx <= c.T.LineWidths.Count ? (long)c.T.LineWidths[idx - 1] : 12700)) / Emu;
        var dash = X.Attr(X.Child(ln, "prstDash"), "val") switch
        {
            "dash" or "lgDash" => new DoubleCollection { 4, 3 },
            "sysDash" => new DoubleCollection { 3, 1 },
            "dot" or "sysDot" => new DoubleCollection { 1, 1 },
            "dashDot" or "lgDashDot" => new DoubleCollection { 4, 2, 1, 2 },
            _ => null,
        };
        var b = Fill(ln, c, out var decided);
        if (decided) return (b, w, dash);
        if (idx > 0) return (Solid(ColorOf(lnRef, c)), w, dash);
        return (null, 0, null);
    }

    static Brush? Solid(Color? c, bool none = false) => c == null || none ? null : new SolidColorBrush(c.Value);

    Brush? BlipBrush(OpenXmlElement blipFill, Ctx c)
    {
        var blip = X.Child(blipFill, "blip");
        var rid = X.Attr(blip, "embed");
        if (rid == null || Image(c.Part, rid) is not { } img) return Brushes.Gainsboro;
        var b = new ImageBrush(img) { Stretch = Stretch.Fill };
        if (X.Child(blipFill, "srcRect") is { } src)
        {
            double l = (X.Long(src, "l") ?? 0) / 100000.0, t = (X.Long(src, "t") ?? 0) / 100000.0,
                   rr = (X.Long(src, "r") ?? 0) / 100000.0, bb = (X.Long(src, "b") ?? 0) / 100000.0;
            b.Viewbox = new Rect(l, t, Math.Max(0.001, 1 - l - rr), Math.Max(0.001, 1 - t - bb));
        }
        if (X.Long(X.Child(blip, "alphaModFix"), "amt") is { } amt) b.Opacity = amt / 100000.0;
        return b;
    }

    ImageSource? Image(OpenXmlPart part, string rid)
    {
        if (!part.TryGetPartById(rid, out var p) || p is not ImagePart ip) return null;
        if (images.TryGetValue(ip.Uri, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            var ms = new MemoryStream();
            using (var s = ip.GetStream()) s.CopyTo(ms);
            ms.Position = 0;
            if (ip.ContentType.Contains("emf") || ip.ContentType.Contains("wmf")) img = Metafile(ms);
            else
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit();
                bi.Freeze();
                img = bi;
            }
        }
        catch (Exception) { }
        return images[ip.Uri] = img;
    }

    /// <summary>WPF can't draw EMF/WMF; rasterize through GDI+ (common for pictures pasted from Office).</summary>
    static ImageSource? Metafile(Stream s)
    {
        using var mf = new System.Drawing.Imaging.Metafile(s);
        int w = Math.Clamp(mf.Width, 1, 2400), h = Math.Clamp(mf.Height, 1, 2400);
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp)) g.DrawImage(mf, 0, 0, w, h);
        var png = new MemoryStream();
        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        png.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = png; bi.EndInit();
        bi.Freeze();
        return bi;
    }

    Color? ColorOf(OpenXmlElement? parent, Ctx c, string? schemeFallback = null)
    {
        var e = parent?.ChildElements.FirstOrDefault(x => x.LocalName is "srgbClr" or "schemeClr" or "sysClr" or "prstClr" or "scrgbClr");
        if (e == null)
            return schemeFallback == null ? null : Scheme(schemeFallback, c);
        Color col = e.LocalName switch
        {
            "srgbClr" => Hex(X.Attr(e, "val")),
            "sysClr" => Hex(X.Attr(e, "lastClr")),
            "schemeClr" => Scheme(X.Attr(e, "val") ?? "tx1", c),
            "prstClr" => (Color?)ColorConverter.ConvertFromString(X.Attr(e, "val") ?? "Black") ?? Colors.Black,
            _ => Color.FromRgb((byte)(255 * (X.Long(e, "r") ?? 0) / 100000), (byte)(255 * (X.Long(e, "g") ?? 0) / 100000), (byte)(255 * (X.Long(e, "b") ?? 0) / 100000)),
        };
        return ColorMath.Apply(col, e.ChildElements.Select(m => (m.LocalName, (X.Long(m, "val") ?? 0) / 100000.0)));
    }

    static Color Scheme(string v, Ctx c)
    {
        if (c.ClrMap.TryGetValue(v, out var mapped)) v = mapped;
        return c.T.Colors.GetValueOrDefault(v, Colors.Black);
    }

    static Color Hex(string? s) =>
        s is { Length: 6 } && int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v) : Colors.Black;

    Theme ThemeOf(SlideMasterPart mp)
    {
        if (themes.TryGetValue(mp, out var t)) return t;
        var el = X.Child(mp.ThemePart?.Theme, "themeElements");
        var colors = new Dictionary<string, Color>();
        foreach (var e in X.Child(el, "clrScheme")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            var v = e.FirstChild;
            colors[e.LocalName] = v?.LocalName == "sysClr" ? Hex(X.Attr(v, "lastClr")) : Hex(X.Attr(v, "val"));
        }
        var fonts = new Dictionary<string, string>();
        var fs = X.Child(el, "fontScheme");
        foreach (var (key, name) in new[] { ("mj", "majorFont"), ("mn", "minorFont") })
        {
            var f = X.Child(fs, name);
            fonts[$"+{key}-lt"] = X.Attr(X.Child(f, "latin"), "typeface") ?? "";
            var hang = f?.ChildElements.FirstOrDefault(x => x.LocalName == "font" && X.Attr(x, "script") == "Hang");
            fonts[$"+{key}-ea"] = X.Attr(hang, "typeface") is { Length: > 0 } h ? h : X.Attr(X.Child(f, "ea"), "typeface") ?? "";
        }
        var widths = X.Child(X.Child(el, "fmtScheme"), "lnStyleLst")?.ChildElements.Select(l => (double)(X.Long(l, "w") ?? 12700)).ToList() ?? [];
        return themes[mp] = new Theme(colors, fonts, widths);
    }

    // ---------- geometry ----------

    static Geometry Geometry(OpenXmlElement? spPr, double w, double h, double extW, double extH)
    {
        if (X.Child(spPr, "custGeom") is { } cust) return Custom(cust, w, h, extW, extH);
        var prst = X.Child(spPr, "prstGeom");
        double Adj(string name, double def) =>
            X.Child(prst, "avLst")?.ChildElements.FirstOrDefault(g => X.Attr(g, "name") == name) is { } gd
            && double.TryParse(X.Attr(gd, "fmla")?.Replace("val ", ""), out var v) ? v / 100000 : def;
        double mn = Math.Min(w, h);
        Geometry Poly(params double[] xy)
        {
            var fig = new PathFigure { StartPoint = new Point(xy[0], xy[1]), IsClosed = true };
            for (int i = 2; i < xy.Length; i += 2) fig.Segments.Add(new LineSegment(new Point(xy[i], xy[i + 1]), true));
            return new PathGeometry(new[] { fig });
        }
        var kind = X.Attr(prst, "prst") ?? "rect";
        if (kind is "line" or "straightConnector1" || kind.StartsWith("bentConnector") || kind.StartsWith("curvedConnector"))
            return new LineGeometry(new Point(0, 0), new Point(w, h));
        double a;
        switch (kind)
        {
            case "ellipse": case "flowChartConnector": return new EllipseGeometry(new Rect(0, 0, w, h));
            case "roundRect": case "flowChartAlternateProcess": a = mn * Adj("adj", 0.16667); return new RectangleGeometry(new Rect(0, 0, w, h), a, a);
            case "flowChartTerminator": return new RectangleGeometry(new Rect(0, 0, w, h), mn / 2, mn / 2);
            case "triangle": return Poly(w * Adj("adj", 0.5), 0, w, h, 0, h);
            case "rtTriangle": return Poly(0, 0, w, h, 0, h);
            case "diamond": case "flowChartDecision": return Poly(w / 2, 0, w, h / 2, w / 2, h, 0, h / 2);
            case "parallelogram": a = mn * Adj("adj", 0.25); return Poly(a, 0, w, 0, w - a, h, 0, h);
            case "trapezoid": a = mn * Adj("adj", 0.25); return Poly(a, 0, w - a, 0, w, h, 0, h);
            case "hexagon": a = mn * Adj("adj", 0.25); return Poly(a, 0, w - a, 0, w, h / 2, w - a, h, a, h, 0, h / 2);
            case "homePlate": a = mn * Adj("adj", 0.5); return Poly(0, 0, w - a, 0, w, h / 2, w - a, h, 0, h);
            case "chevron": a = mn * Adj("adj", 0.5); return Poly(0, 0, w - a, 0, w, h / 2, w - a, h, 0, h, a, h / 2);
            case "rightArrow":
            case "leftArrow":
                double sh = h * Adj("adj1", 0.5), hl = mn * Adj("adj2", 0.5), y0 = (h - sh) / 2, y1 = (h + sh) / 2;
                return kind == "rightArrow"
                    ? Poly(0, y0, w - hl, y0, w - hl, 0, w, h / 2, w - hl, h, w - hl, y1, 0, y1)
                    : Poly(w, y0, hl, y0, hl, 0, 0, h / 2, hl, h, hl, y1, w, y1);
            default: return new RectangleGeometry(new Rect(0, 0, w, h)); // ponytail: other presets drawn as rectangles
        }
    }

    static Geometry Custom(OpenXmlElement cust, double w, double h, double extW, double extH)
    {
        var geo = new PathGeometry();
        foreach (var path in X.Child(cust, "pathLst")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            double sx = w / (X.Long(path, "w") is { } pw && pw > 0 ? pw : extW), sy = h / (X.Long(path, "h") is { } ph && ph > 0 ? ph : extH);
            PathFigure? fig = null;
            Point cur = default;
            Point Pt(OpenXmlElement? pt) => new((X.Long(pt, "x") ?? 0) * sx, (X.Long(pt, "y") ?? 0) * sy);
            foreach (var cmd in path.ChildElements)
            {
                var pts = cmd.ChildElements.Where(e => e.LocalName == "pt").Select(Pt).ToList();
                switch (cmd.LocalName)
                {
                    case "moveTo": fig = new PathFigure { StartPoint = cur = pts[0] }; geo.Figures.Add(fig); break;
                    case "lnTo": fig?.Segments.Add(new LineSegment(cur = pts[0], true)); break;
                    case "cubicBezTo": fig?.Segments.Add(new BezierSegment(pts[0], pts[1], cur = pts[2], true)); break;
                    case "quadBezTo": fig?.Segments.Add(new QuadraticBezierSegment(pts[0], cur = pts[1], true)); break;
                    case "arcTo":
                        double wr = (X.Long(cmd, "wR") ?? 0) * sx, hr = (X.Long(cmd, "hR") ?? 0) * sy;
                        double st = (X.Long(cmd, "stAng") ?? 0) / 60000.0 * Math.PI / 180, swa = (X.Long(cmd, "swAng") ?? 0) / 60000.0 * Math.PI / 180;
                        var center = new Point(cur.X - wr * Math.Cos(st), cur.Y - hr * Math.Sin(st));
                        cur = new Point(center.X + wr * Math.Cos(st + swa), center.Y + hr * Math.Sin(st + swa));
                        fig?.Segments.Add(new ArcSegment(cur, new Size(wr, hr), 0, Math.Abs(swa) > Math.PI,
                            swa > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
                        break;
                    case "close": if (fig != null) fig.IsClosed = true; break;
                }
            }
        }
        return geo;
    }
}

/// <summary>Tiny helpers for walking OpenXML by local name, which tolerates every namespace variant.</summary>
public static class X
{
    public static OpenXmlElement? Child(OpenXmlElement? e, string name) => e?.ChildElements.FirstOrDefault(c => c.LocalName == name);
    public static string? Attr(OpenXmlElement? e, string name) => e?.GetAttributes().FirstOrDefault(a => a.LocalName == name).Value;
    public static long? Long(OpenXmlElement? e, string name) => long.TryParse(Attr(e, name), out var v) ? v : null;
}

static class ColorMath
{
    public static Color Apply(Color c, IEnumerable<(string name, double val)> mods)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, a = 1;
        foreach (var (name, v) in mods)
        {
            switch (name)
            {
                case "alpha": a = v; break;
                case "shade": r *= v; g *= v; b *= v; break;
                case "tint": r = 1 - (1 - r) * v; g = 1 - (1 - g) * v; b = 1 - (1 - b) * v; break;
                case "lumMod" or "lumOff" or "satMod":
                    var (hh, ss, ll) = ToHsl(r, g, b);
                    if (name == "lumMod") ll *= v; else if (name == "lumOff") ll += v; else ss *= v;
                    (r, g, b) = FromHsl(hh, Math.Clamp(ss, 0, 1), Math.Clamp(ll, 0, 1));
                    break;
            }
        }
        static byte B(double x) => (byte)Math.Round(Math.Clamp(x, 0, 1) * 255);
        return Color.FromArgb(B(a), B(r), B(g), B(b));
    }

    static (double h, double s, double l) ToHsl(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        double d = max - min, s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    static (double, double, double) FromHsl(double h, double s, double l)
    {
        if (s == 0) return (l, l, l);
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static double F(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        return (F(p, q, h + 1.0 / 3), F(p, q, h), F(p, q, h - 1.0 / 3));
    }
}
