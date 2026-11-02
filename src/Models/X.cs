using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>Tiny helpers for walking OpenXML by local name, which tolerates every namespace variant.</summary>
public static class X
{
    public static OpenXmlElement? Child(OpenXmlElement? e, string name) => e?.ChildElements.FirstOrDefault(c => c.LocalName == name);
    public static string? Attr(OpenXmlElement? e, string name) => e?.GetAttributes().FirstOrDefault(a => a.LocalName == name).Value;
    public static long? Long(OpenXmlElement? e, string name) => long.TryParse(Attr(e, name), out var v) ? v : null;
}
