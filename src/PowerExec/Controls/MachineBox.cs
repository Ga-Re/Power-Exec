using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PowerExec.Core.Config;
using PowerExec.Core.Localization;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Eingabe für Rechnernamen: beim Tippen von „@“ erscheinen die Rechnergruppen als Vorschläge,
/// der Knopf „Gruppe einfügen ▾“ fügt eine Gruppe mit einem Klick ein. Gebunden wird <see cref="Text"/>.
/// Aussehen: Vorlage in <c>Themes/Controls.xaml</c>.
/// </summary>
[TemplatePart(Name = PartInput, Type = typeof(TextBox))]
[TemplatePart(Name = PartButton, Type = typeof(ButtonBase))]
[TemplatePart(Name = PartPopup, Type = typeof(Popup))]
[TemplatePart(Name = PartList, Type = typeof(ListBox))]
public sealed class MachineBox : Control
{
    private const string PartInput = "PART_Input", PartButton = "PART_Button", PartPopup = "PART_Popup", PartList = "PART_List";

    /// <summary>Rechnergruppen der App (setzt der AppController).</summary>
    public static Func<IReadOnlyList<MachineGroup>>? GroupSource { get; set; }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MachineBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((MachineBox)d).OnTextChanged((string)e.NewValue)));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(MachineBox), new PropertyMetadata(string.Empty));

    static MachineBox() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MachineBox), new FrameworkPropertyMetadata(typeof(MachineBox)));

    public MachineBox() => Placeholder = L.T("Computer names or @group, separated by commas");

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    private TextBox? _input;
    private ButtonBase? _button;
    private Popup? _popup;
    private ListBox? _list;
    private bool _syncing;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_input != null)
        {
            _input.TextChanged -= Input_TextChanged;
            _input.PreviewKeyDown -= Input_PreviewKeyDown;
            _input.LostKeyboardFocus -= Input_LostKeyboardFocus;
        }
        if (_list != null)
        {
            _list.PreviewMouseLeftButtonUp -= List_MouseUp;
            _list.PreviewKeyDown -= List_PreviewKeyDown;
        }
        if (_button != null) _button.Click -= Button_Click;

        _input = GetTemplateChild(PartInput) as TextBox;
        _button = GetTemplateChild(PartButton) as ButtonBase;
        _popup = GetTemplateChild(PartPopup) as Popup;
        _list = GetTemplateChild(PartList) as ListBox;

        if (_input != null)
        {
            _syncing = true;
            _input.Text = Text ?? string.Empty;
            _syncing = false;
            _input.TextChanged += Input_TextChanged;
            _input.PreviewKeyDown += Input_PreviewKeyDown;
            _input.LostKeyboardFocus += Input_LostKeyboardFocus;
        }
        if (_list != null)
        {
            _list.PreviewMouseLeftButtonUp += List_MouseUp;
            _list.PreviewKeyDown += List_PreviewKeyDown;
        }
        if (_button != null) _button.Click += Button_Click;
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _input == null) return;
        _syncing = true;
        SetCurrentValue(TextProperty, _input.Text);
        _syncing = false;
        ShowSuggestions();
    }

    private void Input_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is not ListBoxItem) ClosePopup();
    }

    private void List_MouseUp(object sender, MouseButtonEventArgs e) => Pick();

    private void List_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Tab) { Pick(); e.Handled = true; }
        else if (e.Key == Key.Escape) { ClosePopup(); _input?.Focus(); e.Handled = true; }
    }

    private void Button_Click(object sender, RoutedEventArgs e) => OpenGroupMenu();

    private void ClosePopup()
    {
        if (_popup != null) _popup.IsOpen = false;
    }

    private void OnTextChanged(string text)
    {
        if (_syncing || _input == null) return;
        _syncing = true;
        _input.Text = text ?? string.Empty;
        _syncing = false;
    }

    private static IReadOnlyList<MachineGroup> Groups => GroupSource?.Invoke() ?? Array.Empty<MachineGroup>();

    private static string Label(MachineGroup g) => L.F("@{0}  ·  {1} computers", g.Name, g.Machines.Count);

    private void OpenGroupMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _button, Placement = PlacementMode.Bottom };
        if (Groups.Count == 0) menu.Items.Add(new MenuItem { Header = L.T("No computer groups yet (Settings › Computer groups)"), IsEnabled = false });
        foreach (var g in Groups)
        {
            var item = new MenuItem { Header = Label(g), ToolTip = string.Join(", ", g.Machines.Take(20)) + (g.Machines.Count > 20 ? " …" : string.Empty) };
            item.Click += (_, _) => Insert("@" + g.Name, replaceToken: false);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>Aktuelles Wort vor dem Cursor (getrennt durch Komma, Semikolon, Leerzeichen, Zeilenumbruch).</summary>
    private (int Start, string Token) CurrentToken()
    {
        if (_input == null) return (0, string.Empty);
        var text = _input.Text;
        var caret = _input.CaretIndex;
        int start = caret;
        while (start > 0 && !",; \r\n\t".Contains(text[start - 1])) start--;
        return (start, text[start..caret]);
    }

    private void ShowSuggestions()
    {
        if (_input == null || _popup == null || _list == null) return;
        var (_, token) = CurrentToken();
        if (!token.StartsWith('@') || !_input.IsKeyboardFocused) { _popup.IsOpen = false; return; }
        var prefix = token[1..];
        var matches = Groups.Where(g => g.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase) || (prefix.Length > 0 && g.Name.Contains(prefix, StringComparison.CurrentCultureIgnoreCase)))
            .Select(g => new ListBoxItem { Content = Label(g), Tag = g.Name }).ToList();
        _list.ItemsSource = matches;
        if (matches.Count > 0) _list.SelectedIndex = 0;
        _popup.IsOpen = matches.Count > 0;
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_popup is not { IsOpen: true } || _list == null) return;
        switch (e.Key)
        {
            case Key.Down:
                (_list.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, _list.SelectedIndex)) as UIElement)?.Focus();
                e.Handled = true;
                break;
            case Key.Enter or Key.Tab when _list.SelectedItem != null:
                Pick();
                e.Handled = true;
                break;
            case Key.Escape:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void Pick()
    {
        if (_list?.SelectedItem is ListBoxItem { Tag: string name }) Insert("@" + name, replaceToken: true);
        ClosePopup();
    }

    private void Insert(string value, bool replaceToken)
    {
        if (_input == null) return;
        var text = _input.Text;
        int start, end;
        if (replaceToken) { (start, _) = CurrentToken(); end = _input.CaretIndex; }
        else { start = end = text.Length; }
        var before = text[..start];
        var after = text[end..];
        if (!replaceToken && before.Trim().Length > 0 && !before.TrimEnd().EndsWith(',')) before = before.TrimEnd() + ", ";
        var result = before + value + (after.TrimStart().StartsWith(',') || after.Length == 0 ? string.Empty : ", ") + after;
        _input.Text = result;
        _input.CaretIndex = (before + value).Length;
        _input.Focus();
    }
}
