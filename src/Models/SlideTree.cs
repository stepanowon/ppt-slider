using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace PptSlider;

/// <summary>A slide's shape tree: new shape ids, adding shapes, drawing order.</summary>
static class SlideTree
{
    /// <summary>An id no shape on the slide uses yet.</summary>
    public static uint NextId(SlidePart sp) =>
        sp.Slide!.Descendants<P.NonVisualDrawingProperties>().Select(p => p.Id?.Value ?? 0).DefaultIfEmpty(0u).Max() + 1;

    /// <summary>Adds a shape to the top of the slide's shape tree (before its extLst, which must stay last).</summary>
    public static void AppendShape(SlidePart sp, OpenXmlElement shape)
    {
        var tree = sp.Slide!.CommonSlideData!.ShapeTree!;
        if (X.Child(tree, "extLst") is { } ext) ext.InsertBeforeSelf(shape); else tree.AppendChild(shape);
    }

    static bool IsShape(OpenXmlElement e) => e.LocalName is "sp" or "pic" or "cxnSp" or "grpSp" or "graphicFrame" or "contentPart" or "AlternateContent";

    /// <summary>"front", "back", "forward" (one step up) or "backward": the drawing order of <paramref name="els"/> (siblings).</summary>
    public static void Arrange(IReadOnlyCollection<OpenXmlElement> els, string how)
    {
        if (els.FirstOrDefault()?.Parent is not { } parent) return;
        var set = els.Where(e => e.Parent == parent).ToHashSet();
        var shapes = parent.ChildElements.Where(IsShape).ToList();
        if (shapes.Count < 2) return;
        var order = how switch
        {
            "front" => shapes.Where(s => !set.Contains(s)).Concat(shapes.Where(set.Contains)).ToList(),
            "back" => shapes.Where(set.Contains).Concat(shapes.Where(s => !set.Contains(s))).ToList(),
            _ => shapes.ToList(),
        };
        if (how == "forward")
            for (int i = order.Count - 2; i >= 0; i--)
                if (set.Contains(order[i]) && !set.Contains(order[i + 1])) (order[i], order[i + 1]) = (order[i + 1], order[i]);
        if (how == "backward")
            for (int i = 1; i < order.Count; i++)
                if (set.Contains(order[i]) && !set.Contains(order[i - 1])) (order[i], order[i - 1]) = (order[i - 1], order[i]);
        if (order.SequenceEqual(shapes)) return;
        var anchor = shapes[0].PreviousSibling(); // grpSpPr: shapes come right after it
        foreach (var s in shapes) s.Remove();
        foreach (var s in order)
        {
            if (anchor == null) parent.PrependChild(s); else anchor.InsertAfterSelf(s);
            anchor = s;
        }
    }
}
