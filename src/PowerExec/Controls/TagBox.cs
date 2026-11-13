using PowerExec.Core.Localization;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Desktop.Infrastructure;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Eingabe für Schlagworte als Chips: Enter, Komma oder Semikolon fügt hinzu, × bzw. Rücktaste (leeres Feld) entfernt.
/// Beim Hineinklicken erscheinen alle vorhandenen (globalen) Schlagworte, beim Tippen die passenden (Autovervollständigung).
/// Gebunden wird <see cref="TagsText"/> (kommagetrennt).
/// </summary>
public sealed class TagBox : Control
{
    public static readonly DependencyProperty TagsTextProperty = DependencyProperty.Register(
        nameof(TagsText), typeof(string), typeof(TagBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((TagBox)d).SyncFromText()));

    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions), typeof(IEnumerable), typeof(TagBox), new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(TagBox), new PropertyMetadata(L.T("Type a tag, press Enter"), (d, _) => ((TagBox)d).UpdatePlaceholder()));

    public string TagsText { get => (string)GetValue(TagsTextProperty); set => SetValue(TagsTextProperty, value); }
    public IEnumerable? Suggestions { get => (IEnumerable?)GetValue(SuggestionsProperty); set => SetValue(SuggestionsProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    private readonly List<string> _tags = new();
    private readonly WrapPanel _panel = new() { Margin = new Thickness(4, 2, 4, 2) };
    private readonly TextBox _input;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly Border _border;
    private bool _syncing;

    public TagBox()
    {
        Focusable = false;
        _input = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            MinWidth = 120,
            MinHeight = 26,
            Padding = new Thickness(2, 0, 2, 0),
            Margin = new Thickness(2),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _input.PreviewKeyDown += Input_PreviewKeyDown;
        _input.TextChanged += (_, _) =>
        {
            if (_input.Text.Contains(',') || _input.Text.Contains(';')) AddPending();
            else ShowSuggestions();
        };
        _input.LostKeyboardFocus += (_, e) =>
        {
            if (_rebuilding || e.NewFocus is DependencyObject d && IsInPopup(d)) return;
            AddPending();
            _popup!.IsOpen = false;
        };
        _input.GotKeyboardFocus += (_, _) =>
        {
            _border!.SetResourceReference(Border.BorderBrushProperty, "Accent");
            ShowSuggestions();
        };

        _list = new ListBox { MaxHeight = 180, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        _list.SetResourceReference(ForegroundProperty, "Text.Primary");
        _list.PreviewMouseLeftButtonUp += (_, _) => PickSuggestion();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Tab) { PickSuggestion(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _popup!.IsOpen = false; _input.Focus(); e.Handled = true; }
        };
        var popupBorder = new Border { Child = _list, CornerRadius = Tokens.RadiusCard, BorderThickness = new Thickness(1), Padding = new Thickness(4), MinWidth = 180 };
        popupBorder.SetResourceReference(Border.BackgroundProperty, "Bg.Menu");
        popupBorder.SetResourceReference(Border.BorderBrushProperty, "Border.Control");
        _popup = new Popup { Child = popupBorder, PlacementTarget = _input, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };

        _panel.Children.Add(_input);
        var grid = new Grid();
        grid.Children.Add(_panel);
        grid.Children.Add(_popup);
        _border = new Border { Child = grid, BorderThickness = new Thickness(1), MinHeight = Tokens.HeightControl, Cursor = Cursors.IBeam };
        _border.SetResourceReference(Border.CornerRadiusProperty, Tokens.Keys.RadiusControl);
        _border.SetResourceReference(Border.BackgroundProperty, "Bg.Input");
        _border.SetResourceReference(Border.BorderBrushProperty, "Border.Control");
        _border.MouseLeftButtonDown += (_, e) => { _input.Focus(); e.Handled = true; };
        _input.LostKeyboardFocus += (_, _) => _border.SetResourceReference(Border.BorderBrushProperty, "Border.Control");
        AddVisualChild(_border);
        AddLogicalChild(_border);
        UpdatePlaceholder();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _border;

    protected override Size MeasureOverride(Size constraint)
    {
        _border.Measure(constraint);
        return _border.DesiredSize;
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        _border.Arrange(new Rect(arrangeBounds));
        return arrangeBounds;
    }

    private bool IsInPopup(DependencyObject d)
    {
        for (var c = d; c != null; c = VisualTreeHelper.GetParent(c) ?? LogicalTreeHelper.GetParent(c))
            if (ReferenceEquals(c, _popup.Child)) return true;
        return false;
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when _popup.IsOpen && _input.Text.Trim().Length > 0 && _list.SelectedItem is string pick
                                && pick.StartsWith(_input.Text.Trim().TrimStart('#'), StringComparison.CurrentCultureIgnoreCase):
                _input.Text = string.Empty;
                Add(pick); // Autovervollständigung: Anfang getippt, Enter übernimmt den Vorschlag
                e.Handled = true;
                break;
            case Key.Enter:
                if (_input.Text.Trim().Length > 0) { AddPending(); e.Handled = true; }
                break;
            case Key.Tab when _input.Text.Trim().Length > 0:
                AddPending();
                e.Handled = true;
                break;
            case Key.Back when _input.Text.Length == 0 && _tags.Count > 0:
                Remove(_tags[^1]);
                e.Handled = true;
                break;
            case Key.Down when _popup.IsOpen && _list.Items.Count > 0:
                _list.SelectedIndex = 0;
                (_list.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.Focus();
                e.Handled = true;
                break;
            case Key.Escape when _popup.IsOpen:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void PickSuggestion()
    {
        if (_list.SelectedItem is string tag)
        {
            _input.Text = string.Empty;
            Add(tag);
        }
        _popup.IsOpen = false;
        _input.Focus();
    }

    private void ShowSuggestions()
    {
        var text = _input.Text.Trim().TrimStart('#');
        if (Suggestions == null || !_input.IsKeyboardFocused) { _popup.IsOpen = false; return; }
        var matches = Suggestions.Cast<object>().Select(o => o?.ToString() ?? string.Empty)
            .Where(s => s.Length > 0 && (text.Length == 0 || s.Contains(text, StringComparison.CurrentCultureIgnoreCase)) && !_tags.Contains(s, StringComparer.CurrentCultureIgnoreCase))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(s => text.Length > 0 && s.StartsWith(text, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).ThenBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .Take(text.Length == 0 ? 30 : 12).ToList();
        _list.ItemsSource = matches;
        _popup.IsOpen = matches.Count > 0;
        if (matches.Count > 0 && text.Length > 0) _list.SelectedIndex = 0;
    }

    private void AddPending()
    {
        foreach (var t in Tags.Parse(_input.Text)) Add(t);
        _input.Text = string.Empty;
    }

    private void Add(string tag)
    {
        tag = tag.Trim().TrimStart('#');
        if (tag.Length == 0 || _tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase)) return;
        // Schreibweise eines vorhandenen (globalen) Schlagworts übernehmen
        if (Suggestions?.Cast<object>().Select(o => o?.ToString()).FirstOrDefault(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)) is { } known) tag = known;
        _tags.Add(tag);
        Rebuild();
        PushText();
        Dispatcher.BeginInvoke(ShowSuggestions, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Remove(string tag)
    {
        _tags.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        Rebuild();
        PushText();
    }

    private void PushText()
    {
        _syncing = true;
        TagsText = Tags.Format(_tags);
        _syncing = false;
    }

    private void SyncFromText()
    {
        if (_syncing) return;
        _tags.Clear();
        _tags.AddRange(Tags.Parse(TagsText));
        Rebuild();
    }

    private void UpdatePlaceholder() => _input.Tag = _tags.Count == 0 ? Placeholder : L.T("more …");

    private bool _rebuilding;

    private void Rebuild()
    {
        bool hadFocus = _input.IsKeyboardFocused;
        _rebuilding = true;
        try
        {
            _panel.Children.Clear();
            foreach (var tag in _tags) _panel.Children.Add(Chip(tag));
            _panel.Children.Add(_input);
        }
        finally { _rebuilding = false; }
        UpdatePlaceholder();
        if (hadFocus) _input.Focus();
    }

    private UIElement Chip(string tag)
    {
        var text = new TextBlock { Text = "#" + tag, VerticalAlignment = VerticalAlignment.Center, FontSize = Tokens.FontBody, Margin = new Thickness(8, 0, 2, 1) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
        var remove = new Wpf.Ui.Controls.Button
        {
            Content = new TextBlock { Text = "", FontSize = Tokens.IconXs, FontFamily = TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe MDL2 Assets") },
            Width = 20,
            Height = 20,
            MinHeight = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 2, 0),
            ToolTip = L.T("Remove"),
            Focusable = false,
            Cursor = Cursors.Hand
        };
        remove.SetResourceReference(StyleProperty, "Btn.Icon");
        remove.Click += (_, _) => Remove(tag);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(text);
        panel.Children.Add(remove);
        var chip = new Border { Child = panel, CornerRadius = new CornerRadius(Tokens.HeightChip / 2), Margin = new Thickness(2), Height = Tokens.HeightChip, Cursor = Cursors.Arrow };
        chip.SetResourceReference(Border.BackgroundProperty, "Bg.Chip");
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return chip;
    }
}
