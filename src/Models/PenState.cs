using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;

namespace PptSlider;

public enum Tool { Select, Pen, Highlighter, Eraser, Laser }

/// <summary>Current inking tool, shared by the editor and the slide show window.</summary>
public class PenState
{
    public static readonly Color[] Palette =
        [Colors.Black, Colors.Red, Color.FromRgb(0x1E, 0x5A, 0xF0), Color.FromRgb(0x10, 0xA0, 0x40), Colors.Yellow, Colors.White, Colors.Orange];

    public Tool Tool { get; private set; } = Tool.Select;
    public Color PenColor { get; private set; } = Colors.Red;
    public double PenSize { get; private set; } = 3;
    public Color HiColor { get; private set; } = Colors.Yellow;
    public double HiSize { get; private set; } = 24;
    public event Action? Changed;

    public bool IsHighlighter => Tool == Tool.Highlighter;
    public double Size => IsHighlighter ? HiSize : PenSize;
    public Color Color => IsHighlighter ? HiColor : PenColor;

    public void SetTool(Tool t) { Tool = t; Changed?.Invoke(); }

    /// <summary>Colors the highlighter when it is active, otherwise the pen (and switches to the pen).</summary>
    public void SetColor(Color c)
    {
        if (IsHighlighter) HiColor = c;
        else { PenColor = c; Tool = Tool.Pen; }
        Changed?.Invoke();
    }

    public void SetSize(double s)
    {
        if (IsHighlighter) HiSize = Math.Clamp(s, 6, 80);
        else PenSize = Math.Clamp(s, 1, 40);
        Changed?.Invoke();
    }

    public void Grow(int dir) => SetSize(Size + dir * (IsHighlighter ? 4 : 1));

    public void Apply(InkCanvas ic)
    {
        ic.IsHitTestVisible = Tool is Tool.Pen or Tool.Highlighter or Tool.Eraser;
        ic.EditingMode = Tool switch
        {
            Tool.Pen or Tool.Highlighter => InkCanvasEditingMode.Ink,
            Tool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None,
        };
        ic.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Color,
            Width = IsHighlighter ? HiSize * 0.4 : PenSize,
            Height = Size,
            IsHighlighter = IsHighlighter,
            StylusTip = IsHighlighter ? StylusTip.Rectangle : StylusTip.Ellipse,
            FitToCurve = true,
        };
    }
}
