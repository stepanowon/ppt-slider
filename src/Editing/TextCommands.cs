using System.Windows.Documents;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;

namespace PptSlider;

/// <summary>
/// Text formatting from the ribbon and the shortcuts. While text is being edited it applies to the editor's selection
/// (characters) or the paragraphs it touches; otherwise to all the text of the selected shapes.
/// </summary>
sealed class TextCommands(IEditorHost host, ShapeSelection selection, TextEditor text, SlideEdits edits)
{
    public static readonly double[] FontSizes = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 44, 48, 54, 60, 66, 72, 80, 88, 96];

    /// <summary>
    /// A paragraph setting (bullets, indent, spacing, alignment) on the paragraphs being edited, or else on every paragraph
    /// of the selected shapes. <paramref name="f"/> gets each paragraph with the shape or cell it belongs to.
    /// </summary>
    void ParagraphOp(Action<List<(A.Paragraph P, OpenXmlElement Owner)>> f)
    {
        if (text.Target is { } el)
        {
            var (from, to) = text.SelectedParagraphs();
            edits.Apply(() => f(TextFormat.Paragraphs(el).Skip(from).Take(to - from + 1).Select(p => (p, el)).ToList()));
            text.ReEdit(el, (from, to));
            return;
        }
        edits.OnText(sh => f(TextFormat.Paragraphs(sh).Select(p => (p, p.Ancestors().FirstOrDefault(a => a.LocalName == "tc") ?? sh)).ToList()));
    }

    /// <summary>The size the ribbon shows: of the editor's selection, else of the selected shape's first run.</summary>
    public double SelectedSize => text.IsEditing ? text.SelectedSize
        : selection.Primary is { } el && double.TryParse(TextFormat.FirstRun(el, "sz"), out var v) ? v / 100 : 18;

    public void SetFontSize(double pt)
    {
        if (text.IsEditing) text.Apply(TextElement.FontSizeProperty, pt * text.Scale * 96 / 72);
        else edits.OnText(s => TextFormat.EditRuns(s, r => Ooxml.SetAttr(r, "sz", ((int)Math.Round(pt * 100)).ToString())));
    }

    public void Grow() => SetFontSize(FontSizes.FirstOrDefault(v => v > SelectedSize, SelectedSize + 8));
    public void Shrink() => SetFontSize(FontSizes.LastOrDefault(v => v < SelectedSize, Math.Max(1, SelectedSize - 1)));

    public void SetFontFace(string face)
    {
        if (text.IsEditing) text.Apply(TextElement.FontFamilyProperty, new FontFamily(face));
        else edits.OnText(s => TextFormat.EditRuns(s, r => TextFormat.SetFont(r, face)));
    }

    public void SetColor(Color c)
    {
        if (text.IsEditing) text.Apply(TextElement.ForegroundProperty, new SolidColorBrush(c));
        else edits.OnText(sh => TextFormat.EditRuns(sh, r => TextFormat.SetColor(r, c)));
    }

    public void SetHighlight(Color? c)
    {
        if (text.IsEditing) text.Apply(TextElement.BackgroundProperty, c is { } v ? new SolidColorBrush(v) : null);
        else edits.OnText(sh => TextFormat.EditRuns(sh, r => TextFormat.SetHighlight(r, c)));
    }

    /// <summary>"bold", "italic", "underline" or "strike": on the editor's selection, else on or off for the whole shape.</summary>
    public void Toggle(string id)
    {
        if (text.IsEditing) { text.Toggle(id); return; }
        var (attr, on, off) = id switch
        {
            "bold" => ("b", "1", "0"),
            "italic" => ("i", "1", "0"),
            "underline" => ("u", "sng", "none"),
            _ => ("strike", "sngStrike", "noStrike"),
        };
        if (selection.Primary is not { } first) { edits.OnText(_ => { }); return; }
        var value = TextFormat.FirstRun(first, attr) is { } v && v != off && v != "0" ? off : on;
        edits.OnText(s => TextFormat.EditRuns(s, r => Ooxml.SetAttr(r, attr, value)));
    }

    public void SetAlign(string algn) => ParagraphOp(ps => ps.ForEach(x => TextFormat.SetAlign(x.P, algn)));

    /// <summary>Bullets ("buChar") or numbering ("buAutoNum") on, or off when every paragraph concerned has them already.</summary>
    public void ToggleBullets(string kind) => ParagraphOp(ps =>
    {
        bool all = ps.Count > 0 && ps.All(x => host.Deck!.Renderer.BulletKind(host.CurrentSlide!, x.Owner, x.P) == kind);
        foreach (var x in ps) TextFormat.SetBullet(x.P, all ? null : kind);
    });

    public void Indent(int delta) => ParagraphOp(ps => ps.ForEach(x => TextFormat.Indent(x.P, delta)));
    public void SetLineSpacing(double multiple) => ParagraphOp(ps => ps.ForEach(x => TextFormat.SetLineSpacing(x.P, multiple)));
}
