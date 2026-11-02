using System.Windows.Media;

namespace PptSlider;

/// <summary>
/// Font names from a document (or typed in) on their way to WPF. <see cref="FontFamily"/> reads "location#name" as a font
/// file to load and "a, b" as a list, so a typeface like <c>file://host/share/#X</c> would make WPF fetch a font from that
/// path (an SMB connection that can hand over the user's NTLM hash, and an untrusted font for the font engine to parse)
/// just by drawing the slide. Only plain family names get through.
/// </summary>
public static class FontNames
{
    /// <summary>Characters no installed family name needs, but a font location or a second list entry does.</summary>
    static readonly char[] Unsafe = ['#', '/', '\\', ':', ',', ';', '%', '?', '*', '"', '<', '>', '|'];

    /// <summary>A plain family name: not empty, not too long, no path, URI or list characters, no control characters.</summary>
    public static bool IsSafe(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && name.IndexOfAny(Unsafe) < 0 && !name.Any(char.IsControl);

    /// <summary>The name if it is safe, else null.</summary>
    public static string? Safe(string? name) => IsSafe(name) ? name!.Trim() : null;

    /// <summary>A family trying each safe name in turn, then <paramref name="fallback"/>.</summary>
    public static FontFamily Family(IEnumerable<string?> names, string fallback = "Malgun Gothic") =>
        new(string.Join(", ", names.Select(Safe).OfType<string>().Append(fallback).Distinct()));

    public static FontFamily Family(string? name, string fallback = "Malgun Gothic") => Family([name], fallback);
}
