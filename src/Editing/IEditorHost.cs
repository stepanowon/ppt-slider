using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DocumentFormat.OpenXml.Packaging;

namespace PptSlider;

/// <summary>
/// What the editing parts need from the window that shows the slide. They depend on this, never on the window itself,
/// so each can be built and tested on its own and the window stays a view that wires them together.
/// </summary>
interface IEditorHost
{
    Deck? Deck { get; }
    SlidePart? CurrentSlide { get; }

    /// <summary>The current slide's shapes as last drawn, bottom to top (slide coordinates).</summary>
    IReadOnlyList<ShapeHit> Hits { get; }

    /// <summary>The slide surface, in slide coordinates: mouse capture and the cursor go here.</summary>
    FrameworkElement Surface { get; }

    /// <summary>Drawn over the slide: selection marks and the text editor.</summary>
    Canvas Marks { get; }

    /// <summary>The drawn slide (table cells are found in it); null before a slide is shown.</summary>
    Canvas? Drawing { get; }

    /// <summary>One screen pixel in slide units, so marks keep their size at any zoom.</summary>
    double Px { get; }

    /// <summary>The app's accent color (selection marks, the editor's border).</summary>
    Brush Accent { get; }

    Dispatcher Dispatcher { get; }

    /// <summary>Back to the selection tool (inserting or pasting ends drawing ink).</summary>
    void UseSelectTool();

    /// <summary>After an edit of the current slide: redraw it, its thumbnail, the title, the show.</summary>
    void Changed();

    void Status(string text);

    /// <summary>The status bar back to "slide n of m".</summary>
    void SlideStatus();

    /// <summary>The ribbon's font and size boxes show the selection's.</summary>
    void SyncFormatUi();

    /// <summary>Whether focus in <paramref name="d"/> is in the ribbon's font or size box (the text edit goes on).</summary>
    bool IsFormatInput(DependencyObject? d);

    void Warn(string message, string title);
}
