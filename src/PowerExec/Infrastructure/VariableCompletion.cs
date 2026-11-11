using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PowerExec.Infrastructure;

/// <summary>
/// Vorschläge beim Tippen von <c>{{$</c> in einem Textfeld: Liste der Variablen, Pfeiltasten wählen, Enter/Tab fügt ein,
/// Esc schließt. Der angefangene Name filtert die Liste.
/// </summary>
public static class VariableCompletion
{
    public static void Attach(TextBox box, Func<IReadOnlyList<string>> tokens)
    {
        var list = new ListBox { MaxHeight = 220, MinWidth = 220, FontFamily = (System.Windows.Media.FontFamily)box.FindResource("MonoFont"), BorderThickness = new Thickness(0) };
        var popup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.RelativePoint,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Child = list,
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1)
            }
        };
        if (popup.Child is Border b)
        {
            b.SetResourceReference(Border.BackgroundProperty, "Bg.Menu");
            b.SetResourceReference(Border.BorderBrushProperty, "Border.Control");
        }

        // Anfang des angefangenen {{$… vor dem Cursor, sonst -1
        int Start()
        {
            var caret = box.CaretIndex;
            var before = box.Text[..Math.Min(caret, box.Text.Length)];
            var i = before.LastIndexOf("{{$", StringComparison.Ordinal);
            if (i < 0) return -1;
            var partial = before[(i + 3)..];
            return partial.All(c => char.IsLetterOrDigit(c) || c == '_') ? i : -1;
        }

        void Insert()
        {
            if (list.SelectedItem is not string token) { popup.IsOpen = false; return; }
            var start = Start();
            if (start < 0) { popup.IsOpen = false; return; }
            var caret = box.CaretIndex;
            // schon vorhandene schließende }} hinter dem Cursor nicht verdoppeln
            var after = box.Text[caret..];
            var replaceEnd = caret;
            var tail = new string(after.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            replaceEnd += tail.Length;
            if (box.Text.Length >= replaceEnd + 2 && box.Text.Substring(replaceEnd, 2) == "}}") replaceEnd += 2;
            box.Text = box.Text[..start] + token + box.Text[replaceEnd..];
            box.CaretIndex = start + token.Length;
            popup.IsOpen = false;
            box.Focus();
        }

        void Update()
        {
            var start = Start();
            if (start < 0 || !box.IsKeyboardFocused) { popup.IsOpen = false; return; }
            var partial = box.Text[(start + 3)..box.CaretIndex];
            var matches = tokens().Where(t => t[3..].StartsWith(partial, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (matches.Count == 0) { popup.IsOpen = false; return; }
            list.ItemsSource = matches;
            list.SelectedIndex = 0;
            var rect = box.GetRectFromCharacterIndex(box.CaretIndex);
            popup.HorizontalOffset = double.IsInfinity(rect.Left) ? 0 : rect.Left;
            popup.VerticalOffset = double.IsInfinity(rect.Bottom) ? box.ActualHeight : rect.Bottom + 2;
            popup.IsOpen = true;
        }

        box.TextChanged += (_, _) => Update();
        box.LostKeyboardFocus += (_, e) => { if (e.NewFocus is not DependencyObject d || !list.IsAncestorOf(d)) popup.IsOpen = false; };
        box.PreviewKeyDown += (_, e) =>
        {
            if (!popup.IsOpen) return;
            switch (e.Key)
            {
                case Key.Down: list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; break;
                case Key.Up: list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; break;
                case Key.Enter or Key.Tab: Insert(); e.Handled = true; break;
                case Key.Escape: popup.IsOpen = false; e.Handled = true; break;
            }
        };
        list.PreviewMouseLeftButtonUp += (_, _) => Insert();
    }
}
