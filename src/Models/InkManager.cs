using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using DocumentFormat.OpenXml.Packaging;

namespace PptSlider;

/// <summary>
/// Screen ink: each slide's strokes, the desktop's (while no file is open), the blank screen's in the show, and their undo.
/// Strokes are in the screen coordinates of the overlay they were drawn on (see <see cref="InkOverlay"/>); a stroke drawn
/// over a slide also carries where the slide was (<see cref="TagFrame"/>), so <see cref="OnSlides"/> can put it on the slide.
/// </summary>
public sealed class InkManager
{
    readonly Dictionary<SlidePart, StrokeCollection> slides = new();
    readonly InkHistory history = new();

    /// <summary>Ink while no file is open.</summary>
    public StrokeCollection Desktop { get; } = new();
    /// <summary>Ink on the black / white screen; a new blank screen starts empty (<see cref="NewBlank"/>).</summary>
    public StrokeCollection Blank { get; private set; } = new();
    /// <summary>A stroke was drawn, erased or cleared anywhere.</summary>
    public event Action? Changed;

    public InkManager()
    {
        Watch(Desktop);
        Watch(Blank);
    }

    void Watch(StrokeCollection s)
    {
        history.Track(s);
        s.StrokesChanged += (_, _) => Changed?.Invoke();
    }

    /// <summary>The strokes on <paramref name="slide"/> (an empty collection the first time).</summary>
    public StrokeCollection Of(SlidePart slide)
    {
        if (slides.TryGetValue(slide, out var s)) return s;
        slides[slide] = s = new StrokeCollection();
        Watch(s);
        return s;
    }

    /// <summary>A deleted slide: its strokes and their undo go with it.</summary>
    public void Forget(SlidePart slide)
    {
        if (slides.Remove(slide, out var s)) history.Untrack(s);
    }

    /// <summary>Another file opened: no slide ink, and no undo of earlier strokes.</summary>
    public void Reset()
    {
        slides.Clear();
        history.Clear();
        history.Track(Desktop);
        history.Track(Blank);
    }

    /// <summary>A new blank screen: the old one's strokes can't come back.</summary>
    public void NewBlank()
    {
        history.Untrack(Blank);
        Blank = new StrokeCollection();
        Watch(Blank);
    }

    /// <summary>Takes back the last change to <paramref name="strokes"/>; false when there is none.</summary>
    public bool Undo(StrokeCollection strokes) => history.Undo(strokes);

    /// <summary>Every slide's ink and the desktop's.</summary>
    public void ClearAll()
    {
        foreach (var s in slides.Values) s.Clear();
        Desktop.Clear();
    }

    static readonly Guid FrameId = new("6E3B1C2A-4F4D-4E8B-9C1A-2D7B5E0F9A31");

    /// <summary>Remembers where the slide was (<paramref name="frame"/>, overlay coordinates) when <paramref name="s"/> was drawn.</summary>
    public static void TagFrame(Stroke s, Rect frame) => s.AddPropertyData(FrameId, new[] { frame.X, frame.Y, frame.Width, frame.Height });

    /// <summary>
    /// Each slide's strokes that were drawn over the slide, moved onto it (slide coordinates, the slide <paramref name="w"/> ×
    /// <paramref name="h"/>); slides without any are left out.
    /// </summary>
    public Dictionary<SlidePart, StrokeCollection> OnSlides(IEnumerable<SlidePart> order, double w, double h)
    {
        var result = new Dictionary<SlidePart, StrokeCollection>();
        foreach (var sp in order)
        {
            if (!slides.TryGetValue(sp, out var strokes)) continue;
            var on = new StrokeCollection();
            foreach (var st in strokes)
            {
                if (!st.ContainsPropertyData(FrameId) || st.GetPropertyData(FrameId) is not double[] { Length: 4 } f) continue;
                double sx = w / f[2], sy = h / f[3];
                var c = st.Clone();
                var m = Matrix.Identity;
                m.Translate(-f[0], -f[1]);
                m.Scale(sx, sy);
                c.Transform(m, false);
                c.DrawingAttributes.Width *= sx;
                c.DrawingAttributes.Height *= sy;
                on.Add(c);
            }
            if (on.Count > 0) result[sp] = on;
        }
        return result;
    }
}
