using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DocumentFormat.OpenXml.Packaging;

namespace PptSlider;

static partial class SelfTest
{
    /// <summary>
    /// The slide keys after the font box was used. Real key events go to whatever has keyboard focus, through the window's
    /// PreviewKeyDown, as when a person types. With the caret left in the font box, clicking a thumbnail (which can't take
    /// focus) used to leave it there, so ↑↓ / PgUp / PgDn changed the font box's choice, and that applied a font to the slide.
    /// </summary>
    static void FocusKeyTests(string file, string outDir, Action<bool, string> check)
    {
        var main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, Width = 1400, Height = 860 };
        try
        {
            main.Show();
            main.Activate();
            main.Open(file);
            Wait(100);
            var deck = main.Deck!;
            string Xml() => string.Concat(deck.Slides.Select(s => s.Slide!.OuterXml));
            var before = Xml();
            var fontText = (TextBox)main.FontBox.Template.FindName("PART_EditableTextBox", main.FontBox);

            // what really goes to the focused element: PreviewKeyDown (window first), then KeyDown
            void Press(Key k)
            {
                var target = Keyboard.FocusedElement as IInputElement ?? main;
                var src = PresentationSource.FromVisual(main)!;
                var down = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, k) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                target.RaiseEvent(down);
                if (!down.Handled) target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, k) { RoutedEvent = Keyboard.KeyDownEvent });
                Wait(30);
            }
            // a click on a thumbnail: the mouse goes down on it (it can't take focus) and it becomes the selected slide
            void ClickThumb(int i)
            {
                var item = (ListBoxItem)main.Thumbs.Items[i];
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                main.Thumbs.SelectedIndex = i;
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                Wait(50);
            }

            fontText.Focus();
            Wait(30);
            check(main.FontBox.IsKeyboardFocusWithin, "focus keys: (setup) the caret is in the font box");
            ClickThumb(1);
            check(main.Current == 1 && !main.FontBox.IsKeyboardFocusWithin, $"focus keys: clicking a thumbnail takes the caret out of the font box (focus: {Keyboard.FocusedElement?.GetType().Name ?? "none"}, window focusable {main.Focusable})");
            var seen = new List<int>();
            foreach (var k in new[] { Key.Down, Key.PageDown, Key.PageUp, Key.Up, Key.Right, Key.Left }) { Press(k); seen.Add(main.Current + 1); }
            check(string.Join(",", seen) == "3,4,3,2,3,2", $"focus keys: ↓ PgDn PgUp ↑ → ← after a thumbnail click change the slide ({string.Join(",", seen)})");
            check(Xml() == before && !deck.Dirty, "focus keys: and no font (or anything else) on the slides changed");

            // keys typed while the caret is (still) in the font box don't apply a font either, even with shapes selected
            main.Run("selectAll");
            check(main.Selection.Count > 0, "focus keys: (setup) shapes selected");
            fontText.Focus();
            Wait(30);
            Press(Key.Down);
            Press(Key.PageDown);
            check(Xml() == before && !deck.Dirty, "focus keys: ↓ / PgDn in the font box with its list closed apply no font");
            Press(Key.Escape); // puts the box back and leaves it
            check(!main.FontBox.IsKeyboardFocusWithin && main.FontBox.Text == (TextFormat.FirstRunFont(main.Selection.Primary!) ?? ""), "focus keys: Esc in the font box puts it back and leaves it");

            // a font picked from the list is applied, and the caret goes back so the slide keys work again
            ClickThumb(0);
            main.Run("selectAll");
            var face = main.FontBox.Items.Cast<string>().First(f => f is "Arial" or "Segoe UI");
            main.FontBox.Focus();
            main.FontBox.IsDropDownOpen = true;
            Wait(50);
            main.FontBox.SelectedItem = face;
            main.FontBox.IsDropDownOpen = false;
            Wait(80);
            var picked = main.Selection.Primary is { } el ? TextFormat.FirstRunFont(el) : null;
            check(picked == face && !main.FontBox.IsKeyboardFocusWithin, $"focus keys: a font picked from the list is applied ({picked}) and the caret leaves the box");
            var applied = Xml();
            Press(Key.Escape); // nothing selected
            Press(Key.PageDown);
            check(main.Current == 1 && Xml() == applied, "focus keys: then PgDn changes the slide, not the font");
        }
        finally { main.CloseForTest(); }
    }
}
