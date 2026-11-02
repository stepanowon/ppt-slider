using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PptSlider;

enum DragKind { None, Move, Resize, Rotate, Marquee, Draw }

/// <summary>What a drag in progress shows instead of the selection as it is.</summary>
readonly record struct DragPreview(DragKind Kind, Rect Box = default, Vector Offset = default, double Angle = 0, (Point From, Point To)? Line = null);

/// <summary>
/// Draws the selection over the slide: a dashed box around each selected shape, and for a single one its eight resize
/// handles and the rotate handle; while dragging, where the shapes are going (or the box / line being drawn).
/// </summary>
sealed class SelectionAdorner(IEditorHost host, ShapeSelection selection)
{
    /// <summary>The drag in progress (Kind None: the selection as it is).</summary>
    public DragPreview Preview { get; set; }

    /// <summary>Which edges a handle moves: -1 left / top, 1 right / bottom, 0 neither.</summary>
    public static readonly (int X, int Y)[] Handles = [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];

    public static Point HandlePoint(Rect r, (int X, int Y) h) => new(r.X + r.Width * (h.X + 1) / 2, r.Y + r.Height * (h.Y + 1) / 2);

    /// <summary>The rotate handle: 22 screen pixels above the box (<paramref name="px"/>: one pixel in slide units).</summary>
    public static Point RotateHandle(Rect r, double px) => new(r.X + r.Width / 2, r.Y - 22 * px);

    /// <summary><paramref name="p"/> turned <paramref name="degrees"/> clockwise around the middle of <paramref name="r"/>.</summary>
    public static Point Turn(Point p, Rect r, double degrees) =>
        degrees == 0 ? p : new RotateTransform(degrees, r.X + r.Width / 2, r.Y + r.Height / 2).Transform(p);

    public void Draw()
    {
        var marks = host.Marks;
        foreach (var c in marks.Children.OfType<Shape>().ToList()) marks.Children.Remove(c);
        double px = host.Px;
        var accent = host.Accent;
        void Add(Shape s, double x, double y) { s.IsHitTestVisible = false; Canvas.SetLeft(s, x); Canvas.SetTop(s, y); marks.Children.Add(s); }

        var pv = Preview;
        if (pv.Kind is DragKind.Marquee or DragKind.Draw)
        {
            if (pv.Line is var (a, b))
                Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = accent, StrokeThickness = 2 * px }, 0, 0);
            else
                Add(new Rectangle { Width = pv.Box.Width, Height = pv.Box.Height, Stroke = accent, StrokeThickness = 1 * px, StrokeDashArray = [4, 2],
                    Fill = new SolidColorBrush(Color.FromArgb(0x18, 0xC4, 0x3E, 0x1C)) }, pv.Box.X, pv.Box.Y);
            return;
        }
        var shown = selection.Hits;
        bool single = shown.Count == 1;
        foreach (var h in shown)
        {
            var r = h.Bounds;
            double rot = ShapeLayout.Rotation(h.Element);
            if (pv.Kind == DragKind.Move) r.Offset(pv.Offset);
            if (single && pv.Kind == DragKind.Resize) r = pv.Box;
            if (single && pv.Kind == DragKind.Rotate) rot = pv.Angle;
            Add(new Rectangle
            {
                Width = r.Width, Height = r.Height, Stroke = accent, StrokeThickness = 1.5 * px, StrokeDashArray = [4, 2],
                RenderTransform = rot != 0 ? new RotateTransform(rot, r.Width / 2, r.Height / 2) : null,
            }, r.X, r.Y);
            if (!single) continue;
            foreach (var hd in Handles)
            {
                var c = Turn(HandlePoint(r, hd), r, rot);
                Add(new Rectangle { Width = 9 * px, Height = 9 * px, Fill = Brushes.White, Stroke = accent, StrokeThickness = 1.5 * px }, c.X - 4.5 * px, c.Y - 4.5 * px);
            }
            if (ShapeLayout.CanRotate(h.Element))
            {
                var c = Turn(RotateHandle(r, px), r, rot);
                Add(new Ellipse { Width = 11 * px, Height = 11 * px, Fill = Brushes.White, Stroke = accent, StrokeThickness = 1.5 * px }, c.X - 5.5 * px, c.Y - 5.5 * px);
            }
        }
    }
}
