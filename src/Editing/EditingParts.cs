namespace PptSlider;

/// <summary>
/// The editing parts built for one window and wired together: each gets only the parts it works with (constructor
/// injection), and all of them reach the window only through <see cref="IEditorHost"/>.
/// </summary>
sealed class EditingParts
{
    public ShapeSelection Selection { get; }
    public SelectionAdorner Adorner { get; }
    public TextEditor TextEditor { get; }
    public StageInteraction StageInput { get; }
    public ShapeCommands Shapes { get; }
    public TextCommands Text { get; }
    public TableCommands Tables { get; }
    public InsertCommands Insert { get; }
    public ClipboardCommands Clipboard { get; }
    public FormatPainterTool Painter { get; }

    public EditingParts(IEditorHost host)
    {
        Selection = new ShapeSelection(host);
        Adorner = new SelectionAdorner(host, Selection);
        TextEditor = new TextEditor(host, Selection, Adorner);
        var edits = new SlideEdits(host, Selection, TextEditor);
        Painter = new FormatPainterTool(host, Selection, TextEditor, edits);
        StageInput = new StageInteraction(host, Selection, Adorner, TextEditor, Painter);
        Shapes = new ShapeCommands(host, Selection, Adorner, TextEditor, edits);
        Text = new TextCommands(host, Selection, TextEditor, edits);
        Tables = new TableCommands(host, Selection, TextEditor, edits);
        Insert = new InsertCommands(host, Selection, TextEditor, edits);
        Clipboard = new ClipboardCommands(host, Selection, TextEditor, edits, Insert);
    }
}
