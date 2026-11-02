using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PptSlider;

/// <summary>
/// A shape's text. Text taller than its shape overflows it, as in PowerPoint (below a top-anchored box, both ways when
/// centered, above when bottom-anchored), instead of being cut off at the box. The slide's own edge still clips.
/// </summary>
public sealed class TextPanel : StackPanel
{
    protected override Geometry? GetLayoutClip(Size layoutSlotSize) => null;
}
