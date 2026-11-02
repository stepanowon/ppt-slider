using System.Windows;
using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>A top-level shape on the slide, where it was drawn (slide DIPs) and its drawn text, if any.</summary>
public record ShapeHit(OpenXmlElement Element, Rect Bounds, FrameworkElement? Text = null);
