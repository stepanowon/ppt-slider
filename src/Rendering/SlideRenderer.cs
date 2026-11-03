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

/// <summary>
/// Draws PPTX slides as WPF visuals: backgrounds, master/layout graphics, shapes, text, pictures, groups, tables.
/// SmartArt is drawn from the shapes PowerPoint saved with it (drawing.xml).
/// Text layout follows PowerPoint where WPF differs (see SelfTest.Fidelity, checked against PowerPoint's own export:
/// `PptSlider.exe --render deck.pptx outDir` beside PowerPoint's Slide.Export).
/// ponytail: charts, effects, table styles other than the default and animations are not rendered; add when decks need them.
/// </summary>
public class SlideRenderer
{
    public const double Emu = 9525; // EMU per WPF DIP (96 dpi)
    readonly PresentationPart pres;
    /// <summary>Decoded images by part, not by part name: a name can stand for another picture after its part is gone.</summary>
    readonly Dictionary<ImagePart, ImageSource?> images = new(ReferenceEqualityComparer.Instance);
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
        OpenXmlElement? LayoutTree, OpenXmlElement? MasterTree, OpenXmlElement? TxStyles, OpenXmlElement? DefaultText, int Num = 0);

    readonly record struct Map(double Ox, double Oy, double Sx, double Sy)
    {
        public Rect R(OpenXmlElement xfrm)
        {
            var off = X.Child(xfrm, "off"); var ext = X.Child(xfrm, "ext");
            return new Rect(Ox + (X.Long(off, "x") ?? 0) * Sx, Oy + (X.Long(off, "y") ?? 0) * Sy,
                Math.Max(0, (X.Long(ext, "cx") ?? 0) * Sx), Math.Max(0, (X.Long(ext, "cy") ?? 0) * Sy));
        }
    }

    static OpenXmlElement? Tree(OpenXmlElement? root) => X.Child(X.Child(root, "cSld"), "spTree");

    Ctx CtxOf(SlidePart slide)
    {
        var layout = slide.SlideLayoutPart!;
        var master = layout.SlideMasterPart!;
        var clrMap = X.Child(master.SlideMaster, "clrMap")?.GetAttributes().ToDictionary(a => a.LocalName, a => a.Value ?? "")
                     ?? new Dictionary<string, string>();
        var relId = pres.GetIdOfPart(slide);
        int num = pres.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().Select(s => s.RelationshipId?.Value).ToList().IndexOf(relId) ?? -1;
        return new Ctx(slide, ThemeOf(master), clrMap, Tree(layout.SlideLayout), Tree(master.SlideMaster),
            X.Child(master.SlideMaster, "txStyles"), X.Child(pres.Presentation, "defaultTextStyle"), num + 1);
    }

    public Canvas Render(SlidePart slide, List<ShapeHit>? hits = null)
    {
        var layout = slide.SlideLayoutPart!;
        var master = layout.SlideMasterPart!;
        var slideCtx = CtxOf(slide);
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
            var geom = ShapeGeometry.Of(spPr, r.Width, r.Height, (X.Long(X.Child(xfrm, "ext"), "cx") ?? 1), (X.Long(X.Child(xfrm, "ext"), "cy") ?? 1));
            var path = new Path { Data = geom, Fill = fill, Stroke = stroke, StrokeThickness = sw, StrokeDashArray = dash };
            bool fh = X.Attr(xfrm, "flipH") == "1", fv = X.Attr(xfrm, "flipV") == "1";
            if (fh || fv) { path.RenderTransformOrigin = new Point(0.5, 0.5); path.RenderTransform = new ScaleTransform(fh ? -1 : 1, fv ? -1 : 1); }
            // a horizontal or vertical line has no height or width: in the Grid, layout would clip its stroke away entirely
            if (r.Width < 1 || r.Height < 1) host.Children.Add(new Canvas { Children = { path } });
            else host.Children.Add(path);
            if (geom is LineGeometry && stroke != null && X.Child(spPr, "ln") is { } ln)
            {
                // the line as drawn (flips applied): head at its start, tail at its end
                Point a = new(fh ? r.Width : 0, fv ? r.Height : 0), b = new(fh ? 0 : r.Width, fv ? 0 : r.Height);
                foreach (var (end, from, to) in new[] { ("tailEnd", a, b), ("headEnd", b, a) })
                {
                    if (X.Attr(X.Child(ln, end), "type") is null or "none") continue;
                    var dir = to - from;
                    if (dir.Length < 0.01) continue;
                    dir.Normalize();
                    double len = Math.Max(sw * 3, 6);
                    var n = new Vector(-dir.Y, dir.X) * len / 2;
                    var back = to - dir * len;
                    var fig = new PathFigure(to, [new LineSegment(back + n, true), new LineSegment(back - n, true)], true);
                    host.Children.Add(new Path { Data = new PathGeometry([fig]), Fill = stroke });
                }
            }
        }

        if (X.Child(el, "txBody") is { } tx)
        {
            var lists = Lists(tx, chain, phType, c);
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

    /// <summary>The list styles a shape's paragraphs take their level settings from, nearest first.</summary>
    static List<OpenXmlElement?> Lists(OpenXmlElement tx, List<OpenXmlElement> chain, string? phType, Ctx c)
    {
        var lists = new List<OpenXmlElement?> { X.Child(tx, "lstStyle") };
        lists.AddRange(chain.Select(s => X.Child(X.Child(s, "txBody"), "lstStyle")));
        if (phType != null)
            lists.Add(X.Child(c.TxStyles, NormPh(phType) switch { "title" => "titleStyle", "body" => "bodyStyle", _ => "otherStyle" }));
        else { lists.Add(c.DefaultText); lists.Add(X.Child(c.TxStyles, "otherStyle")); }
        return lists;
    }

    /// <summary>The bullet paragraph <paramref name="p"/> of a shape or table cell shows: "buChar", "buAutoNum" or null (none).</summary>
    public string? BulletKind(SlidePart slide, OpenXmlElement shape, OpenXmlElement p)
    {
        var c = CtxOf(slide);
        List<OpenXmlElement?> lists = [c.DefaultText, X.Child(c.TxStyles, "otherStyle")];
        if (shape.LocalName != "tc" && X.Child(shape, "txBody") is { } tx)
        {
            var (chain, phType) = PhChain(shape, c);
            lists = Lists(tx, chain, phType, c);
        }
        var pPr = X.Child(p, "pPr");
        int lvl = (int)(X.Long(pPr, "lvl") ?? 0);
        var levels = lists.Select(l => X.Child(l, $"lvl{lvl + 1}pPr")).Prepend(pPr).OfType<OpenXmlElement>();
        var def = levels.Select(l => l.ChildElements.FirstOrDefault(ch => ch.LocalName is "buNone" or "buChar" or "buAutoNum")).FirstOrDefault(v => v != null);
        return def?.LocalName is "buChar" or "buAutoNum" ? def.LocalName : null;
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
        // the table style's text (tcTxStyle fontRef minor) comes before the presentation's default text style
        var lists = new List<OpenXmlElement?> { TableText, X.Child(c.TxStyles, "otherStyle"), c.DefaultText };
        // PowerPoint's default table style "Medium Style 2 - Accent 1"; ponytail: other table styles render unstyled
        var tblPr = X.Child(tbl, "tblPr");
        var styleId = X.Child(tblPr, "tableStyleId")?.InnerText;
        bool medium2 = styleId is null or "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}";
        bool firstRow = X.Attr(tblPr, "firstRow") is "1" or "true", band = X.Attr(tblPr, "bandRow") is "1" or "true";
        bool firstCol = X.Attr(tblPr, "firstCol") is "1" or "true";
        var accent = Scheme("accent1", c);
        Color Tint(double t) => ColorMath.Apply(accent, [("tint", t)]);
        int ri = 0;
        foreach (var tr in tbl.ChildElements.Where(e => e.LocalName == "tr"))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            double h = (X.Long(tr, "h") ?? 0) * m.Sy;
            bool headerRow = medium2 && firstRow && ri == 0;
            int dataRow = ri - (firstRow ? 1 : 0);
            int ci = 0;
            foreach (var tc in tr.ChildElements.Where(e => e.LocalName == "tc"))
            {
                bool header = headerRow || medium2 && firstCol && ci == 0;
                Brush? styleFill = !medium2 ? null : new SolidColorBrush(header ? accent : band && dataRow % 2 == 0 ? Tint(0.4) : Tint(0.2));
                if (X.Attr(tc, "hMerge") == "1" || X.Attr(tc, "vMerge") == "1") { ci++; continue; }
                var tcPr = X.Child(tc, "tcPr");
                var fill = Fill(tcPr, c, out var hasFill);
                var cell = new Border
                {
                    Tag = tc, // for finding the cell under the mouse
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

    /// <summary>What a text body says for all its paragraphs: its autofit scale and line reduction, wrapping, a style's font color.</summary>
    sealed record TextBody(Ctx C, double FontScale, long LnReduce, bool Wrap, Color? FontRef);

    /// <summary>
    /// A paragraph's properties as it inherits them: its own pPr, then its level in each list style (the shape's, the
    /// layout's, the master's...), each level's defRPr giving the run defaults.
    /// </summary>
    sealed class ParaStyle
    {
        readonly List<OpenXmlElement> levels = [];
        readonly List<OpenXmlElement> defs;

        public ParaStyle(OpenXmlElement p, List<OpenXmlElement?> lists)
        {
            var pPr = X.Child(p, "pPr");
            int lvl = (int)(X.Long(pPr, "lvl") ?? 0);
            if (pPr != null) levels.Add(pPr);
            levels.AddRange(lists.Select(l => X.Child(l, $"lvl{lvl + 1}pPr")).OfType<OpenXmlElement>());
            defs = levels.Select(l => X.Child(l, "defRPr")).OfType<OpenXmlElement>().ToList();
        }

        /// <summary>A paragraph attribute (algn, marL, indent...).</summary>
        public string? Attr(string n) => levels.Select(l => X.Attr(l, n)).FirstOrDefault(v => v != null);
        /// <summary>A paragraph child (lnSpc, spcBef, buClr...).</summary>
        public OpenXmlElement? Child(string n) => levels.Select(l => X.Child(l, n)).FirstOrDefault(v => v != null);
        /// <summary>A run attribute: the run's own (<paramref name="rPr"/>), else the paragraph's default.</summary>
        public string? Run(OpenXmlElement? rPr, string n) => X.Attr(rPr, n) ?? defs.Select(d => X.Attr(d, n)).FirstOrDefault(v => v != null);
        /// <summary>A run's typeface for <paramref name="script"/> ("latin" / "ea"), the theme's minor font by default.</summary>
        public string Face(OpenXmlElement? rPr, string script) =>
            X.Attr(X.Child(rPr, script), "typeface") ?? defs.Select(d => X.Attr(X.Child(d, script), "typeface")).FirstOrDefault(v => v != null) ?? (script == "latin" ? "+mn-lt" : "+mn-ea");
        public IEnumerable<OpenXmlElement> Defaults => defs;
        /// <summary>buNone / buChar / buAutoNum, the nearest one that says.</summary>
        public OpenXmlElement? BulletDef => levels.Select(l => l.ChildElements.FirstOrDefault(ch => ch.LocalName is "buNone" or "buChar" or "buAutoNum")).FirstOrDefault(v => v != null);
    }

    static double Size(ParaStyle st, OpenXmlElement? rPr, TextBody body) => (long.TryParse(st.Run(rPr, "sz"), out var s) ? s : 1800) / 100.0 * body.FontScale * 96 / 72;

    Brush Fore(ParaStyle st, OpenXmlElement? rPr, TextBody body)
    {
        var c = body.C;
        var col = ColorOf(X.Child(rPr, "solidFill"), c) ?? body.FontRef
                  ?? st.Defaults.Select(d => ColorOf(X.Child(d, "solidFill"), c)).FirstOrDefault(v => v != null)
                  ?? ColorOf(null, c, schemeFallback: "tx1");
        return new SolidColorBrush(col ?? Colors.Black);
    }

    static FontFamily Font(ParaStyle st, OpenXmlElement? rPr, Ctx c)
    {
        string Res(string f) => f.StartsWith('+') ? c.T.Fonts.GetValueOrDefault(f, "") : f;
        return FontNames.Family([Res(st.Face(rPr, "latin")), Res(st.Face(rPr, "ea"))]);
    }

    /// <summary>The text of a shape or cell; the panel's Tag is the font scale it was drawn at (see <see cref="Fit"/>).</summary>
    FrameworkElement? Text(OpenXmlElement? tx, List<OpenXmlElement?> lists, List<OpenXmlElement?> bodies, Ctx c, Color? fontRef, double shrink = 1)
    {
        if (tx == null) return null;
        string? B(string n) => bodies.Select(b => X.Attr(b, n)).FirstOrDefault(v => v != null);
        // bodyPr uses lIns/tIns/..., table cells (tcPr) use marL/marT/...
        double Ins(string n, long d) => (long.TryParse(B(n) ?? B("mar" + char.ToUpper(n[0])), out var v) ? v : d) / Emu;
        var autofit = X.Child(X.Child(tx, "bodyPr"), "normAutofit");
        var body = new TextBody(c,
            FontScale: (X.Long(autofit, "fontScale") ?? 100000) / 100000.0 * shrink,
            LnReduce: X.Long(autofit, "lnSpcReduction") ?? 0, // "shrink on overflow" also tightens the lines
            Wrap: B("wrap") != "none",
            FontRef: fontRef);

        var panel = new TextPanel
        {
            Tag = body.FontScale,
            Margin = new Thickness(Ins("lIns", 91440), Ins("tIns", 45720), Ins("rIns", 91440), Ins("bIns", 45720)),
            VerticalAlignment = B("anchor") switch { "ctr" => VerticalAlignment.Center, "b" => VerticalAlignment.Bottom, _ => VerticalAlignment.Top },
        };
        int autoNum = 0;
        bool first = true;
        foreach (var p in tx.ChildElements.Where(e => e.LocalName == "p"))
        {
            panel.Children.Add(Paragraph(p, new ParaStyle(p, lists), body, first, ref autoNum));
            first = false;
        }
        return panel;
    }

    /// <summary>One paragraph: its spacing and line height, its bullet, its runs. <paramref name="autoNum"/>: the numbered list's count so far.</summary>
    TextBlock Paragraph(OpenXmlElement p, ParaStyle st, TextBody body, bool first, ref int autoNum)
    {
        var runs = p.ChildElements.Where(e => e.LocalName is "r" or "br" or "fld").ToList();
        var firstRPr = runs.Select(r => X.Child(r, "rPr")).FirstOrDefault(r => r != null) ?? X.Child(p, "endParaRPr");
        double size = Size(st, firstRPr, body);
        // a line is as tall as its largest text
        double lineSize = runs.Where(r => r.LocalName != "br").Select(r => Size(st, X.Child(r, "rPr"), body)).DefaultIfEmpty(size).Max();
        var tb = new TextBlock
        {
            TextWrapping = body.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextAlignment = st.Attr("algn") switch { "ctr" => TextAlignment.Center, "r" => TextAlignment.Right, "just" or "dist" => TextAlignment.Justify, _ => TextAlignment.Left },
            FontSize = size, FontFamily = Font(st, firstRPr, body.C), Foreground = Fore(st, firstRPr, body),
        };
        double Spc(string n)
        {
            var s = st.Child(n);
            if (X.Child(s, "spcPts") is { } pts) return (X.Long(pts, "val") ?? 0) / 100.0 * 96 / 72;
            if (X.Child(s, "spcPct") is { } pct) return lineSize * 1.2 * (X.Long(pct, "val") ?? 0) / 100000.0;
            return 0;
        }
        double marL = (long.TryParse(st.Attr("marL"), out var ml) ? ml : 0) / Emu, indent = (long.TryParse(st.Attr("indent"), out var ind) ? ind : 0) / Emu;
        // PowerPoint leaves out the space before a box's first paragraph
        // PowerPoint keeps a word on the line when its last glyph just touches the box edge; WPF also counts the glyph's
        // trailing side bearing, so left-aligned text gets that much room past the edge
        double slack = tb.TextAlignment is TextAlignment.Left or TextAlignment.Justify && body.Wrap ? -lineSize * 0.4 : 0;
        tb.Margin = new Thickness(Math.Max(0, marL + indent), first ? 0 : Spc("spcBef"), slack, Spc("spcAft"));
        // PowerPoint's single line is 1.2 × the font size, whatever the font's own line gap (WPF's default would use that)
        tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        tb.LineHeight = Math.Max(1, X.Child(st.Child("lnSpc"), "spcPts") is { } lnPts
            ? (X.Long(lnPts, "val") ?? 0) / 100.0 * 96 / 72 * body.FontScale
            : lineSize * 1.2 * ((X.Long(X.Child(st.Child("lnSpc"), "spcPct"), "val") ?? 100000) - body.LnReduce) / 100000.0);

        bool hasText = runs.Any(r => r.LocalName != "br" && !string.IsNullOrEmpty(X.Child(r, "t")?.InnerText));
        AddBullet(tb, st, body, size, firstRPr, hasText, marL, indent, ref autoNum);
        AddRuns(tb, runs, st, body);
        return tb;
    }

    /// <summary>The paragraph's bullet or number, hanging in its first line's indent when it has one.</summary>
    void AddBullet(TextBlock tb, ParaStyle st, TextBody body, double size, OpenXmlElement? firstRPr, bool hasText, double marL, double indent, ref int autoNum)
    {
        var bulletDef = st.BulletDef;
        if (bulletDef?.LocalName != "buAutoNum") autoNum = 0;
        if (!hasText || bulletDef is not { LocalName: "buChar" or "buAutoNum" }) return;
        var bu = new Run(bulletDef.LocalName == "buChar" ? X.Attr(bulletDef, "char") ?? "•" : $"{++autoNum}.")
        {
            Foreground = Solid(ColorOf(st.Child("buClr"), body.C)) ?? Fore(st, firstRPr, body),
            FontSize = size * (X.Long(st.Child("buSzPct"), "val") ?? 100000) / 100000.0,
        };
        if (X.Attr(st.Child("buFont"), "typeface") is { } bf && !bf.StartsWith('+') && FontNames.Safe(bf) is { } face) bu.FontFamily = new FontFamily(face);
        bu.Tag = Bullet;
        double hang = -indent;
        if (hang >= 1 && marL >= hang)
        {
            // hanging indent: the bullet sits in the first line's indent, every line of text starts at marL
            tb.Margin = tb.Margin with { Left = marL };
            tb.Inlines.Add(HangingBullet(bu, hang));
        }
        else
        {
            tb.Inlines.Add(bu);
            tb.Inlines.Add(new Run("  ") { FontSize = size * 0.6, Tag = Bullet });
        }
    }

    /// <summary>The paragraph's runs, line breaks and fields, with PowerPoint's wrapping and character spacing.</summary>
    void AddRuns(TextBlock tb, List<OpenXmlElement> runs, ParaStyle st, TextBody body)
    {
        var c = body.C;
        // PowerPoint wraps Korean between any two syllables when eaLnBrk and latinLnBrk are both on, otherwise only at spaces
        bool anyBreak = st.Attr("latinLnBrk") is "1" or "true" && st.Attr("eaLnBrk") is null or "1" or "true";
        char prev = ' ';
        double natural = 0, spacing = 0; // the runs' width without and with their character spacing (spc)
        foreach (var r in runs)
        {
            if (r.LocalName == "br") { tb.Inlines.Add(new LineBreak()); prev = ' '; continue; }
            var rPr = X.Child(r, "rPr");
            var s = r.LocalName == "fld" && X.Attr(r, "type") == "slidenum" && c.Num > 0 ? c.Num.ToString() : X.Child(r, "t")?.InnerText ?? "";
            s = Wrapping(s, anyBreak, ref prev);
            var run = new Run(s)
            {
                FontSize = Size(st, rPr, body), FontFamily = Font(st, rPr, c), Foreground = Fore(st, rPr, body),
            };
            // only set when on, so a table header can still make the whole cell bold
            if (st.Run(rPr, "b") is "1" or "true") run.FontWeight = FontWeights.Bold;
            if (st.Run(rPr, "i") is "1" or "true") run.FontStyle = FontStyles.Italic;
            var deco = new TextDecorationCollection();
            if (st.Run(rPr, "u") is { } u && u != "none") foreach (var d in TextDecorations.Underline) deco.Add(d);
            if (st.Run(rPr, "strike") is "sngStrike" or "dblStrike") foreach (var d in TextDecorations.Strikethrough) deco.Add(d);
            if (deco.Count > 0) run.TextDecorations = deco;
            if (X.Child(rPr, "highlight") is { } hl && ColorOf(hl, c) is { } hc) run.Background = new SolidColorBrush(hc);
            tb.Inlines.Add(run);
            var plain = Plain(s);
            if (plain.Length > 0)
            {
                double w = new FormattedText(plain, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, FontStretches.Normal), run.FontSize, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
                natural += w;
                spacing += long.TryParse(st.Run(rPr, "spc"), out var spc) ? plain.Length * spc / 100.0 * 96 / 72 * body.FontScale : 0;
            }
        }
        // WPF has no character spacing: squeeze or stretch the paragraph so its lines are as long as PowerPoint's
        if (spacing != 0 && natural > 0)
            tb.LayoutTransform = new ScaleTransform(Math.Clamp((natural + spacing) / natural, 0.7, 1.3), 1);
    }

    static readonly object Bullet = new();

    /// <summary>Table text takes the theme's minor font (the table style's tcTxStyle fontRef), at every level.</summary>
    static readonly OpenXmlElement TableText = new DocumentFormat.OpenXml.Drawing.ListStyle(
        "<a:lstStyle xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" + string.Concat(Enumerable.Range(1, 9).Select(i =>
        $"<a:lvl{i}pPr><a:defRPr><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/></a:defRPr></a:lvl{i}pPr>")) + "</a:lstStyle>");

    /// <summary>Zero-width space and word joiner: make WPF wrap where PowerPoint would; taken out again for editing.</summary>
    public const char Wrap = '​', NoWrap = '⁠';

    /// <summary>The text without the marks <see cref="Wrapping"/> added.</summary>
    public static string Plain(string s) => s.Replace(Wrap.ToString(), "").Replace(NoWrap.ToString(), "");

    static bool Cjk(char ch) => ch is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏' or >= '一' and <= '鿿' or >= '぀' and <= 'ヿ';

    /// <summary>
    /// WPF breaks lines between any two Korean syllables; PowerPoint does that only when <paramref name="anyBreak"/>
    /// (latinLnBrk="1"), and never inside a Latin word or between a word and the syllable glued to it ("diff를").
    /// <paramref name="prev"/>: the last character before <paramref name="s"/> in its paragraph.
    /// </summary>
    static string Wrapping(string s, bool anyBreak, ref char prev)
    {
        var sb = new System.Text.StringBuilder(s.Length * 2);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) && char.IsLetterOrDigit(prev) && (Cjk(ch) || Cjk(prev)))
                sb.Append(anyBreak && Cjk(ch) && Cjk(prev) ? Wrap : NoWrap);
            else if (ch is '(' or '[' or '{' && !char.IsWhiteSpace(prev))
                sb.Append(NoWrap); // PowerPoint keeps "f(x" and "items[*]" together
            sb.Append(prev = ch);
        }
        return sb.ToString();
    }

    /// <summary>A bullet drawn <paramref name="hang"/> to the left of where its line's text starts, taking no room in the line.</summary>
    static InlineUIContainer HangingBullet(Run bullet, double hang)
    {
        var box = new TextBlock(bullet) { Width = hang, Margin = new Thickness(-hang, 0, 0, 0), Tag = hang };
        return new InlineUIContainer(box) { BaselineAlignment = BaselineAlignment.Baseline, Tag = Bullet };
    }

    /// <summary>
    /// The text a <see cref="Text"/> panel drew, as an editable document that looks the same.
    /// Bullets become non-text inline objects, so the document's text matches <see cref="TextContent.GetEditText"/>.
    /// </summary>
    public static FlowDocument EditDocument(FrameworkElement textPanel)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0) };
        foreach (var tb in ((Panel)textPanel).Children.OfType<TextBlock>())
        {
            var p = new Paragraph
            {
                Margin = tb.Margin with { Right = Math.Max(0, tb.Margin.Right) }, // a paragraph takes no negative margin (the wrap slack)
                TextAlignment = tb.TextAlignment, LineHeight = tb.LineHeight, LineStackingStrategy = tb.LineStackingStrategy,
                FontSize = tb.FontSize, FontFamily = tb.FontFamily, Foreground = tb.Foreground,
            };
            TextBlock? bullet = null;
            foreach (var inl in tb.Inlines)
            {
                if (inl is LineBreak) { p.Inlines.Add(new LineBreak()); continue; }
                if (inl is InlineUIContainer { Tag: var t, Child: TextBlock { Tag: double hang } hb } && t == Bullet && hb.Inlines.FirstInline is Run hr)
                {
                    p.Inlines.Add(HangingBullet(new Run(hr.Text) { FontSize = hr.FontSize, FontFamily = hr.FontFamily, Foreground = hr.Foreground }, hang));
                    continue;
                }
                if (inl is not Run r) continue;
                var copy = new Run(Plain(r.Text))
                {
                    FontSize = r.FontSize, FontFamily = r.FontFamily, Foreground = r.Foreground,
                    FontWeight = r.FontWeight, FontStyle = r.FontStyle, TextDecorations = r.TextDecorations, Background = r.Background,
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
        w = Math.Max(w, 1); // PowerPoint draws a hairline at least a pixel wide; thinner, it would fade to almost nothing
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
        if (images.TryGetValue(ip, out var cached)) return cached;
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
        return images[ip] = img;
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
}
