using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Win32;

namespace PptSlider;

/// <summary>The slide thumbnails: building, partial updates, drag to reorder.</summary>
public partial class MainWindow
{
    void RebuildThumbs()
    {
        syncing = true;
        Thumbs.Items.Clear();
        for (int i = 0; i < Count; i++) Thumbs.Items.Add(NewThumb(i));
        syncing = false;
        UpdateTitle();
    }

    ListBoxItem NewThumb(int i)
    {
        var item = new ListBoxItem { Content = ThumbContent(i), Focusable = false };
        // a ListBoxItem only selects itself on click by taking focus, which a non-focusable one can't: handle the click here
        item.PreviewMouseLeftButtonDown += (_, e) => { Go(Thumbs.Items.IndexOf(item)); thumbDrag = e.GetPosition(Thumbs); e.Handled = true; };
        item.PreviewMouseMove += (_, e) => OnThumbDrag(item, e);
        // right-click: go to that slide, then the menu acts on it
        item.PreviewMouseRightButtonDown += (_, _) => Go(Thumbs.Items.IndexOf(item));
        item.ContextMenu = ThumbMenu();
        return item;
    }

    // Partial updates: only the slide that is new gets rendered; the others keep their picture and get renumbered.

    /// <summary>After a slide was inserted at <paramref name="i"/>.</summary>
    void InsertThumb(int i)
    {
        syncing = true;
        Thumbs.Items.Insert(i, NewThumb(i));
        syncing = false;
        RenumberThumbs(i + 1, Count - 1);
        UpdateTitle();
    }

    /// <summary>After slide <paramref name="i"/> was deleted.</summary>
    void RemoveThumb(int i)
    {
        syncing = true;
        Thumbs.Items.RemoveAt(i);
        syncing = false;
        RenumberThumbs(i, Count - 1);
        UpdateTitle();
    }

    /// <summary>After slide <paramref name="from"/> was moved to index <paramref name="to"/>.</summary>
    void MoveThumb(int from, int to)
    {
        syncing = true;
        var item = Thumbs.Items[from];
        Thumbs.Items.RemoveAt(from);
        Thumbs.Items.Insert(to, item);
        syncing = false;
        RenumberThumbs(Math.Min(from, to), Math.Max(from, to));
        UpdateTitle();
    }

    void RenumberThumbs(int first, int last)
    {
        for (int i = first; i <= last && i < Thumbs.Items.Count; i++)
            if (Thumbs.Items[i] is ListBoxItem { Content: DockPanel { Children: [TextBlock num, ..] } }) num.Text = (i + 1).ToString();
    }

    // ---------- thumbnails: drag to change the slide order ----------

    const string SlideFormat = "PptSlider.Slide";
    Point? thumbDrag; // where the button went down on a thumbnail

    void OnThumbDrag(ListBoxItem item, MouseEventArgs e)
    {
        if (thumbDrag is not { } start || e.LeftButton != MouseButtonState.Pressed) { thumbDrag = null; return; }
        var d = e.GetPosition(Thumbs) - start;
        if (Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance && Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance) return;
        thumbDrag = null;
        DragDrop.DoDragDrop(item, new DataObject(SlideFormat, Thumbs.Items.IndexOf(item)), DragDropEffects.Move);
        ShowDropMark(-1);
    }

    /// <summary>Where a slide dropped at <paramref name="p"/> (on the list) goes: 0 .. Count, before the slide at that index.</summary>
    internal int DropIndex(Point p)
    {
        for (int i = 0; i < Thumbs.Items.Count; i++)
            if (Thumbs.Items[i] is ListBoxItem it && it.IsVisible && p.Y < it.TranslatePoint(new Point(0, it.ActualHeight / 2), Thumbs).Y) return i;
        return Thumbs.Items.Count;
    }

    /// <summary>A line above thumbnail <paramref name="at"/> (below the last one for Count; -1 = none).</summary>
    void ShowDropMark(int at)
    {
        for (int i = 0; i < Thumbs.Items.Count; i++)
            if (Thumbs.Items[i] is ListBoxItem it)
            {
                it.BorderBrush = (Brush)FindResource("Accent");
                it.BorderThickness = new Thickness(0, i == at ? 3 : 0, 0, i == Thumbs.Items.Count - 1 && at == Thumbs.Items.Count ? 3 : 0);
            }
    }

    void OnThumbsDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(SlideFormat)) return;
        e.Effects = DragDropEffects.Move;
        ShowDropMark(DropIndex(e.GetPosition(Thumbs)));
        e.Handled = true;
    }

    void OnThumbsDragLeave(object sender, DragEventArgs e) => ShowDropMark(-1);

    void OnThumbsDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(SlideFormat) is not int from) return;
        e.Handled = true;
        ShowDropMark(-1);
        MoveSlide(from, DropIndex(e.GetPosition(Thumbs)));
    }

    /// <summary>Moves slide <paramref name="from"/> to before the slide now at <paramref name="before"/> (Count = the end).</summary>
    internal void MoveSlide(int from, int before)
    {
        if (Deck == null || from < 0 || from >= Count) return;
        int to = before > from ? before - 1 : before;
        if (to == from) return;
        CommitEdit();
        Deck.MoveSlide(from, to);
        MoveThumb(from, to);
        Current = -1;
        Go(to);
    }

    ContextMenu ThumbMenu()
    {
        var delete = new MenuItem { Header = L.T("deleteSlide"), Icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets") } };
        delete.Click += OnDeleteSlide;
        var menu = new ContextMenu { Items = { delete } };
        menu.Opened += (_, _) => delete.IsEnabled = Count > 1;
        return menu;
    }

    FrameworkElement ThumbContent(int i)
    {
        var num = new TextBlock { Text = (i + 1).ToString(), Width = 22, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Top };
        var view = new Border
        {
            BorderBrush = Brushes.Silver, BorderThickness = new Thickness(1), Background = Brushes.White,
            Child = new Viewbox { Width = 168, Child = Deck!.Renderer.Render(Deck.Slides[i]) },
        };
        var dp = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(num, Dock.Left);
        dp.Children.Add(num);
        dp.Children.Add(view);
        return dp;
    }
}
