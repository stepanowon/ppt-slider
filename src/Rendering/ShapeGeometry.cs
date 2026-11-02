using System.Windows;
using System.Windows.Media;
using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>Shape outlines: the preset geometries PowerPoint names (rect, ellipse, arrows, stars...) and custom paths.</summary>
static class ShapeGeometry
{
    /// <summary>The outline of a shape (its spPr) at w x h; a custom geometry is scaled from its ext (extW x extH, EMU).</summary>
    public static Geometry Of(OpenXmlElement? spPr, double w, double h, double extW, double extH)
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
            case "upArrow":
            case "downArrow":
            {
                double shaft = w * Adj("adj1", 0.5), head = mn * Adj("adj2", 0.5), x0 = (w - shaft) / 2, x1 = (w + shaft) / 2;
                return kind == "upArrow"
                    ? Poly(x0, h, x0, head, 0, head, w / 2, 0, w, head, x1, head, x1, h)
                    : Poly(x0, 0, x1, 0, x1, h - head, w, h - head, w / 2, h, 0, h - head, x0, h - head);
            }
            case "pentagon": return Poly(w / 2, 0, w, h * 0.382, w * 0.809, h, w * 0.191, h, 0, h * 0.382);
            case "star5":
            {
                // ten points on two circles, stretched to fill the box
                var pts = Enumerable.Range(0, 10).Select(i => new Point(Math.Cos(-Math.PI / 2 + i * Math.PI / 5) * (i % 2 == 0 ? 1 : 0.382), Math.Sin(-Math.PI / 2 + i * Math.PI / 5) * (i % 2 == 0 ? 1 : 0.382))).ToList();
                double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                return Poly(pts.SelectMany(p => new[] { (p.X - minX) / (maxX - minX) * w, (p.Y - minY) / (maxY - minY) * h }).ToArray());
            }
            default: return new RectangleGeometry(new Rect(0, 0, w, h)); // ponytail: other presets drawn as rectangles
        }
    }

    /// <summary>A preset shape's outline at a size, for the Shapes gallery.</summary>
    public static Geometry Preset(string prst, double w, double h) =>
        Of(new DocumentFormat.OpenXml.Presentation.ShapeProperties(
            $"""<p:spPr xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><a:prstGeom prst="{prst}"><a:avLst/></a:prstGeom></p:spPr>"""), w, h, 1, 1);

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
