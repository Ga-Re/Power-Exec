using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Core.Localization;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Eingabe für Schlagworte als Chips: Enter, Komma oder Semikolon fügt hinzu, × bzw. Rücktaste (leeres Feld) entfernt.
/// Beim Hineinklicken erscheinen alle vorhandenen (globalen) Schlagworte, beim Tippen die passenden (Autovervollständigung).
/// Gebunden wird <see cref="TagsText"/> (kommagetrennt). Aussehen: Vorlage in <c>Themes/Controls.xaml</c>;
/// die Chips entstehen aus <see cref="ChipTemplate"/>, ihr × löst <see cref="RemoveTagCommand"/> aus.
/// </summary>
[TemplatePart(Name = PartInput, Type = typeof(TextBox))]
[TemplatePart(Name = PartPanel, Type = typeof(Panel))]
[TemplatePart(Name = PartPopup, Type = typeof(Popup))]
[TemplatePart(Name = PartList, Type = typeof(ListBox))]
public sealed class TagBox : Control
{
    private const string PartInput = "PART_Input", PartPanel = "PART_Panel", PartPopup = "PART_Popup", PartList = "PART_List";

    /// <summary>Entfernt das Schlagwort im Befehlsparameter (× eines Chips).</summary>
    public static readonly RoutedCommand RemoveTagCommand = new(nameof(RemoveTagCommand), typeof(TagBox));

    public static readonly DependencyProperty TagsTextProperty = DependencyProperty.Register(
        nameof(TagsText), typeof(string), typeof(TagBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((TagBox)d).SyncFromText()));

    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions), typeof(IEnumerable), typeof(TagBox), new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(TagBox), new PropertyMetadata(string.Empty, (d, _) => ((TagBox)d).UpdatePlaceholder()));

    /// <summary>Aussehen eines Chips; Datenkontext ist das Schlagwort (ohne #).</summary>
    public static readonly DependencyProperty ChipTemplateProperty = DependencyProperty.Register(
        nameof(ChipTemplate), typeof(DataTemplate), typeof(TagBox), new PropertyMetadata(null, (d, _) => ((TagBox)d).Rebuild()));

    static TagBox() => DefaultStyleKeyProperty.OverrideMetadata(typeof(TagBox), new FrameworkPropertyMetadata(typeof(TagBox)));

    public TagBox()
    {
        Placeholder = L.T("Type a tag, press Enter");
        CommandBindings.Add(new CommandBinding(RemoveTagCommand, (_, e) => { if (e.Parameter is string tag) Remove(tag); }));
    }

    public string TagsText { get => (string)GetValue(TagsTextProperty); set => SetValue(TagsTextProperty, value); }
    public IEnumerable? Suggestions { get => (IEnumerable?)GetValue(SuggestionsProperty); set => SetValue(SuggestionsProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public DataTemplate? ChipTemplate { get => (DataTemplate?)GetValue(ChipTemplateProperty); set => SetValue(ChipTemplateProperty, value); }

    private readonly List<string> _tags = new();
    private Panel? _panel;
    private TextBox? _input;
    private Popup? _popup;
    private ListBox? _list;
    private bool _syncing;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_input != null)
        {
            _input.PreviewKeyDown -= Input_PreviewKeyDown;
            _input.TextChanged -= Input_TextChanged;
            _input.LostKeyboardFocus -= Input_LostKeyboardFocus;
            _input.GotKeyboardFocus -= Input_GotKeyboardFocus;
        }
        if (_list != null)
        {
            _list.PreviewMouseLeftButtonUp -= List_MouseUp;
            _list.PreviewKeyDown -= List_PreviewKeyDown;
        }

        _input = GetTemplateChild(PartInput) as TextBox;
        _panel = GetTemplateChild(PartPanel) as Panel;
        _popup = GetTemplateChild(PartPopup) as Popup;
        _list = GetTemplateChild(PartList) as ListBox;

        if (_input != null)
        {
            _input.PreviewKeyDown += Input_PreviewKeyDown;
            _input.TextChanged += Input_TextChanged;
            _input.LostKeyboardFocus += Input_LostKeyboardFocus;
            _input.GotKeyboardFocus += Input_GotKeyboardFocus;
        }
        if (_list != null)
        {
            _list.PreviewMouseLeftButtonUp += List_MouseUp;
            _list.PreviewKeyDown += List_PreviewKeyDown;
        }
        Rebuild();
    }

    /// <summary>Klick irgendwo ins Feld (nicht auf einen Chip) setzt den Cursor in die Eingabe.</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || _input == null) return;
        _input.Focus();
        e.Handled = true;
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_input == null) return;
        if (_input.Text.Contains(',') || _input.Text.Contains(';')) AddPending();
        else ShowSuggestions();
    }

    private void Input_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_rebuilding || e.NewFocus is DependencyObject d && IsInPopup(d)) return;
        AddPending();
        ClosePopup();
    }

    private void Input_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ShowSuggestions();

    private void List_MouseUp(object sender, MouseButtonEventArgs e) => PickSuggestion();

    private void List_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Tab) { PickSuggestion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { ClosePopup(); _input?.Focus(); e.Handled = true; }
    }

    private void ClosePopup()
    {
        if (_popup != null) _popup.IsOpen = false;
    }

    private bool IsInPopup(DependencyObject d)
    {
        for (var c = d; c != null; c = (c is Visual ? VisualTreeHelper.GetParent(c) : null) ?? LogicalTreeHelper.GetParent(c))
            if (ReferenceEquals(c, _popup?.Child)) return true;
        return false;
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_input == null || _popup == null || _list == null) return;
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
        if (_input == null) return;
        if (_list?.SelectedItem is string tag)
        {
            _input.Text = string.Empty;
            Add(tag);
        }
        ClosePopup();
        _input.Focus();
    }

    private void ShowSuggestions()
    {
        if (_input == null || _popup == null || _list == null) return;
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
        if (_input == null) return;
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

    private void UpdatePlaceholder()
    {
        if (_input != null) _input.Tag = _tags.Count == 0 ? Placeholder : L.T("more …");
    }

    private bool _rebuilding;

    /// <summary>Chips neu aufbauen: je Schlagwort ein Element aus <see cref="ChipTemplate"/>, die Eingabe steht am Ende.</summary>
    private void Rebuild()
    {
        if (_panel == null || _input == null) return;
        bool hadFocus = _input.IsKeyboardFocused;
        _rebuilding = true;
        try
        {
            _panel.Children.Clear();
            foreach (var tag in _tags) _panel.Children.Add(new ContentPresenter { Content = tag, ContentTemplate = ChipTemplate });
            _panel.Children.Add(_input);
        }
        finally { _rebuilding = false; }
        UpdatePlaceholder();
        if (hadFocus) _input.Focus();
    }
}
