using System.Windows.Media;

namespace PptSlider;

/// <summary>DrawingML color transforms (alpha, shade, tint, lumMod / lumOff, satMod).</summary>
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
