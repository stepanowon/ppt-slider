using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PptSlider;

/// <summary>
/// The ink part of a toolbar (the editor's ribbon, the show's, the presenter view's, the overlay's): its tool buttons and
/// pen color swatches, which set the shared <see cref="PenState"/> and show what it is.
/// </summary>
sealed class PenToolbar
{
    readonly PenState pen;
    readonly (ToggleButton Button, Tool Tool)[] tools;

    /// <param name="laser">Null on a toolbar without a laser button.</param>
    public PenToolbar(PenState pen, ToggleButton select, ToggleButton? laser, ToggleButton draw, ToggleButton highlighter, ToggleButton eraser)
    {
        this.pen = pen;
        tools = new (ToggleButton?, Tool)[] { (select, Tool.Select), (laser, Tool.Laser), (draw, Tool.Pen), (highlighter, Tool.Highlighter), (eraser, Tool.Eraser) }
            .Where(t => t.Item1 != null).Select(t => (t.Item1!, t.Item2)).ToArray();
    }

    /// <summary>One button per pen color (with the color's shortcut in its tooltip) into <paramref name="swatches"/>.</summary>
    public PenToolbar Swatches(Panel swatches, double size, Thickness margin, Brush border)
    {
        foreach (var (c, i) in PenState.Palette.Select((c, i) => (c, i)))
        {
            var b = new Button { Width = size, Height = size, Margin = margin, Background = new SolidColorBrush(c), BorderBrush = border, Focusable = false, ToolTip = L.T("penColor") };
            Shortcuts.SetFor(b, $"color{i + 1}");
            b.Click += (_, _) => pen.SetColor(c);
            swatches.Children.Add(b);
        }
        return this;
    }

    /// <summary>A tool button was clicked: that tool (the pointer for anything else).</summary>
    public void Pick(object button) => pen.SetTool(tools.FirstOrDefault(t => t.Button == button) is { Button: not null } t ? t.Tool : Tool.Select);

    /// <summary>Checks the button of <paramref name="shown"/> (the pen's tool unless the toolbar shows it as another).</summary>
    public void Sync(Tool? shown = null)
    {
        var tool = shown ?? pen.Tool;
        foreach (var (b, t) in tools) b.IsChecked = t == tool;
    }
}
