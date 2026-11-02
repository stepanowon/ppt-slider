using System.Collections;
using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>
/// The selected shapes (top-level shapes of the current slide), in the order they were picked; the last one is the one
/// formatting reads from. Also the table cell last clicked or edited, where rows and columns go in.
/// </summary>
sealed class ShapeSelection(IEditorHost host) : IReadOnlyList<OpenXmlElement>
{
    readonly List<OpenXmlElement> items = [];

    public int Count => items.Count;
    public OpenXmlElement this[int i] => items[i];
    public IEnumerator<OpenXmlElement> GetEnumerator() => items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The one formatting reads from; setting it selects just that shape (null: nothing).</summary>
    public OpenXmlElement? Primary
    {
        get => items.Count > 0 ? items[^1] : null;
        set { items.Clear(); if (value != null) items.Add(value); }
    }

    /// <summary>The table cell last clicked or edited.</summary>
    public OpenXmlElement? Cell { get; set; }

    public void Set(IEnumerable<OpenXmlElement> els)
    {
        items.Clear();
        items.AddRange(els);
    }

    public void Add(OpenXmlElement el) { if (!items.Contains(el)) items.Add(el); }

    /// <summary>Shift / Ctrl+click: in if it was out, out if it was in.</summary>
    public void Toggle(OpenXmlElement el) { if (!items.Remove(el)) items.Add(el); }

    /// <summary>Nothing selected, no current cell.</summary>
    public void Reset()
    {
        items.Clear();
        Cell = null;
    }

    /// <summary>Where the primary shape is drawn.</summary>
    public ShapeHit? PrimaryHit => host.Hits.FirstOrDefault(h => h.Element == Primary);

    /// <summary>Where the selected shapes are drawn, bottom to top.</summary>
    public List<ShapeHit> Hits => host.Hits.Where(h => items.Contains(h.Element)).ToList();

    /// <summary>After a redraw: shapes that are no longer on the slide are no longer selected.</summary>
    public void Prune() => items.RemoveAll(e => host.Hits.All(h => h.Element != e));
}
