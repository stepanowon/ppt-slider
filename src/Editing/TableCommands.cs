using DocumentFormat.OpenXml;

namespace PptSlider;

/// <summary>Row and column commands on the selected table, at its current cell (its first cell when none was clicked).</summary>
sealed class TableCommands(IEditorHost host, ShapeSelection selection, TextEditor text, SlideEdits edits)
{
    /// <summary>Runs <paramref name="f"/> (frame, cell, row, column) on the selected table at its current cell.</summary>
    public void Apply(Action<OpenXmlElement, OpenXmlElement, int, int> f)
    {
        text.Commit();
        if (host.Deck == null || selection.Primary is not { } frame || !Tables.IsTable(frame)) { host.Status(L.T("selectTableFirst")); return; }
        var tc = selection.Cell != null && selection.Cell.Ancestors().Contains(frame) ? selection.Cell : Tables.Cell(frame, 0, 0);
        var (row, col) = Tables.CellIndex(tc);
        edits.Apply(() =>
        {
            f(frame, tc, row, col);
            if (frame.Parent == null) selection.Primary = null; // the table went with its last row / column
            if (selection.Cell?.Parent?.Parent == null) selection.Cell = null;
        });
    }

    public void RowAbove() => Apply((f, _, r, _) => Tables.InsertRow(f, r, false));
    public void RowBelow() => Apply((f, _, r, _) => Tables.InsertRow(f, r, true));
    public void ColumnLeft() => Apply((f, _, _, c) => Tables.InsertColumn(f, c, false));
    public void ColumnRight() => Apply((f, _, _, c) => Tables.InsertColumn(f, c, true));
    public void DeleteRow() => Apply((f, _, r, _) => { if (!Tables.DeleteRow(f, r)) f.Remove(); selection.Cell = null; });
    public void DeleteColumn() => Apply((f, _, _, c) => { if (!Tables.DeleteColumn(f, c)) f.Remove(); selection.Cell = null; });
    public void DeleteTable() => Apply((f, _, _, _) => f.Remove());
}
