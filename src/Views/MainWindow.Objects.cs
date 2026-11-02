using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PptSlider;

/// <summary>The ribbon's drop-downs and buttons for shapes: clipboard, arrange / align / rotate, fill / outline, the Shapes gallery, tables, format painter.</summary>
public partial class MainWindow
{
    // ---------- drop-downs ----------

    /// <summary>A plain drop-down menu of (text, action) under <paramref name="target"/>; "-" is a separator.</summary>
    static void Menu(object target, params (string Text, Action Do)[] items)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)target, Placement = PlacementMode.Bottom };
        foreach (var (text, act) in items)
        {
            if (text == "-") { menu.Items.Add(new Separator()); continue; }
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => act();
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>A drop-down of color swatches under <paramref name="target"/>; <paramref name="none"/> adds a "no color" entry.</summary>
    static ContextMenu ColorMenu(UIElement target, IEnumerable<Color> colors, Action<Color?> pick, string? none = null)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        var panel = new WrapPanel { Width = 168 };
        foreach (var c in colors)
        {
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(2), Background = new SolidColorBrush(c), BorderBrush = Brushes.Gray, Focusable = false };
            b.Click += (_, _) => { menu.IsOpen = false; pick(c); };
            panel.Children.Add(b);
        }
        menu.Items.Add(panel);
        if (none != null)
        {
            var item = new MenuItem { Header = none };
            item.Click += (_, _) => pick(null);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
        return menu;
    }

    // ---------- clipboard ----------

    // while text is being edited these work on the text, as Ctrl+C / X / V do there
    void OnCut(object s, RoutedEventArgs e) { if (TextEditor.Box is { } box) ApplicationCommands.Cut.Execute(null, box); else ClipboardCommands.Copy(true); }
    void OnCopy(object s, RoutedEventArgs e) { if (TextEditor.Box is { } box) ApplicationCommands.Copy.Execute(null, box); else ClipboardCommands.Copy(false); }
    void OnPaste(object s, RoutedEventArgs e)
    {
        if (TextEditor.Box is { } box) ApplicationCommands.Paste.Execute(null, box);
        else if (!ClipboardCommands.Paste()) StatusText.Text = L.T("nothingToPaste");
    }

    // ---------- arrange, align, fill, outline, rotate ----------

    static readonly Color[] ShapeColors =
    [
        Colors.White, Colors.Black, Color.FromRgb(0xE7, 0xE6, 0xE6), Color.FromRgb(0x44, 0x54, 0x6A), Color.FromRgb(0x44, 0x72, 0xC4),
        Color.FromRgb(0xED, 0x7D, 0x31), Color.FromRgb(0xA5, 0xA5, 0xA5), Color.FromRgb(0xFF, 0xC0, 0x00), Color.FromRgb(0x5B, 0x9B, 0xD5),
        Color.FromRgb(0x70, 0xAD, 0x47), Color.FromRgb(0xC0, 0x00, 0x00), Colors.Red, Color.FromRgb(0xFF, 0xFF, 0x00), Color.FromRgb(0x92, 0xD0, 0x50),
        Color.FromRgb(0x00, 0xB0, 0x50), Color.FromRgb(0x00, 0xB0, 0xF0), Color.FromRgb(0x00, 0x70, 0xC0), Color.FromRgb(0x00, 0x20, 0x60),
        Color.FromRgb(0x70, 0x30, 0xA0), Color.FromRgb(0xDE, 0xEB, 0xF7), Color.FromRgb(0xFB, 0xE5, 0xD6),
    ];

    void OnShapeFill(object sender, RoutedEventArgs e) => ColorMenu((UIElement)sender, ShapeColors, ShapeCommands.SetFill, L.T("noFill"));

    void OnShapeOutline(object sender, RoutedEventArgs e)
    {
        var menu = ColorMenu((UIElement)sender, ShapeColors, ShapeCommands.SetOutline, L.T("noOutline"));
        menu.Items.Add(new Separator());
        var weight = new MenuItem { Header = L.T("outlineWeight") };
        foreach (var pt in new[] { 0.25, 0.5, 0.75, 1, 1.5, 2.25, 3, 4.5, 6 })
        {
            var item = new MenuItem { Header = $"{pt} pt" };
            item.Click += (_, _) => ShapeCommands.SetOutlineWidth(pt);
            weight.Items.Add(item);
        }
        menu.Items.Add(weight);
        var dashes = new MenuItem { Header = L.T("outlineDash") };
        foreach (var (val, key) in new[] { ("solid", "dashSolid"), ("sysDot", "dashDot"), ("dash", "dashDash"), ("lgDashDot", "dashDashDot") })
        {
            var item = new MenuItem { Header = L.T(key) };
            item.Click += (_, _) => ShapeCommands.SetDash(val);
            dashes.Items.Add(item);
        }
        menu.Items.Add(dashes);
    }

    (string, Action)[] RotateItems =>
        [(L.T("rotateRight"), () => ShapeCommands.Rotate(90)), (L.T("rotateLeft"), () => ShapeCommands.Rotate(-90)),
         (L.T("flipV"), () => ShapeCommands.Flip(false)), (L.T("flipH"), () => ShapeCommands.Flip(true))];

    (string, Action)[] AlignItems =>
        [(L.T("alignLeftObj"), () => ShapeCommands.Align("left")), (L.T("alignCenterObj"), () => ShapeCommands.Align("center")), (L.T("alignRightObj"), () => ShapeCommands.Align("right")),
         ("-", () => { }),
         (L.T("alignTopObj"), () => ShapeCommands.Align("top")), (L.T("alignMiddleObj"), () => ShapeCommands.Align("middle")), (L.T("alignBottomObj"), () => ShapeCommands.Align("bottom"))];

    void OnRotateMenu(object sender, RoutedEventArgs e) => Menu(sender, [.. RotateItems[..2], ("-", () => { }), .. RotateItems[2..]]);

    void OnArrangeMenu(object sender, RoutedEventArgs e) => Menu(sender,
        [(L.T("bringToFront"), () => ShapeCommands.Arrange("front")), (L.T("bringForward"), () => ShapeCommands.Arrange("forward")),
         (L.T("sendBackward"), () => ShapeCommands.Arrange("backward")), (L.T("sendToBack"), () => ShapeCommands.Arrange("back")), ("-", () => { }),
         .. AlignItems.Where(i => i.Item1 != "-"), ("-", () => { }), .. RotateItems]);

    void OnFront(object s, RoutedEventArgs e) => ShapeCommands.Arrange("front");
    void OnBack(object s, RoutedEventArgs e) => ShapeCommands.Arrange("back");
    void OnForward(object s, RoutedEventArgs e) => ShapeCommands.Arrange("forward");
    void OnBackward(object s, RoutedEventArgs e) => ShapeCommands.Arrange("backward");
    void OnAlignMenu(object sender, RoutedEventArgs e) => Menu(sender, AlignItems);

    // ---------- shapes gallery ----------

    void OnShapes(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        var panel = new WrapPanel { Width = 7 * 34 };
        foreach (var kind in ShapeFactory.ShapeKinds)
        {
            var icon = new Grid { Width = 22, Height = 22 };
            if (kind is "line" or "arrow")
            {
                icon.Children.Add(new Line { X1 = 3, Y1 = 19, X2 = 19, Y2 = 3, Stroke = Brushes.DimGray, StrokeThickness = 1.5 });
                if (kind == "arrow") icon.Children.Add(new Polygon { Points = [new(19, 3), new(12, 5), new(17, 10)], Fill = Brushes.DimGray });
            }
            else
                icon.Children.Add(new Path { Data = ShapeGeometry.Preset(kind, 20, 20), Margin = new Thickness(1), Stroke = Brushes.DimGray, StrokeThickness = 1.2,
                    Fill = new SolidColorBrush(Color.FromRgb(0xDD, 0xE6, 0xF4)) });
            var b = new Button { Content = icon, Width = 32, Height = 30, Margin = new Thickness(1), ToolTip = L.T("shape." + kind), Focusable = false,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            b.Click += (_, _) => { menu.IsOpen = false; StageInput.StartShape(kind); };
            panel.Children.Add(b);
        }
        menu.Items.Add(panel);
        menu.IsOpen = true;
    }

    // ---------- tables ----------

    void OnInsertTable(object sender, RoutedEventArgs e)
    {
        const int rows = 8, cols = 10;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        var label = new TextBlock { Text = L.T("insertTable"), Margin = new Thickness(2, 0, 0, 4) };
        var grid = new UniformGrid { Rows = rows, Columns = cols };
        var boxes = new Border[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var b = boxes[r, c] = new Border { Width = 17, Height = 17, Margin = new Thickness(1), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White };
                int rr = r, cc = c;
                b.MouseEnter += (_, _) =>
                {
                    for (int i = 0; i < rows; i++)
                        for (int j = 0; j < cols; j++)
                            boxes[i, j].Background = i <= rr && j <= cc ? new SolidColorBrush(Color.FromRgb(0xF4, 0xC7, 0xB8)) : Brushes.White;
                    label.Text = L.F("tableSize", cc + 1, rr + 1);
                };
                b.MouseLeftButtonUp += (_, _) => { menu.IsOpen = false; InsertCommands.Table(rr + 1, cc + 1); };
                grid.Children.Add(b);
            }
        menu.Items.Add(new StackPanel { Children = { label, grid } });
        menu.IsOpen = true;
    }

    void OnRowAbove(object s, RoutedEventArgs e) => TableCommands.RowAbove();
    void OnRowBelow(object s, RoutedEventArgs e) => TableCommands.RowBelow();
    void OnColLeft(object s, RoutedEventArgs e) => TableCommands.ColumnLeft();
    void OnColRight(object s, RoutedEventArgs e) => TableCommands.ColumnRight();
    void OnDeleteRow(object s, RoutedEventArgs e) => TableCommands.DeleteRow();
    void OnDeleteCol(object s, RoutedEventArgs e) => TableCommands.DeleteColumn();
    void OnDeleteTable(object s, RoutedEventArgs e) => TableCommands.DeleteTable();

    // ---------- format painter ----------

    /// <summary>The brush: the selected shape's format goes to the next shape clicked (a shape picked to draw is dropped).</summary>
    internal void OnFormatPainter(object? sender, RoutedEventArgs? e)
    {
        if (Selection.Primary != null) StageInput.CancelShape();
        FormatPainterTool.Start();
    }
}
