using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PowerExec.Core.Config;

namespace PowerExec.Controls;

/// <summary>
/// Eingabe für Rechnernamen: beim Tippen von „@“ erscheinen die Rechnergruppen als Vorschläge,
/// der Knopf „Gruppe einfügen ▾“ fügt eine Gruppe mit einem Klick ein. Gebunden wird <see cref="Text"/>.
/// </summary>
public sealed class MachineBox : Control
{
    /// <summary>Rechnergruppen der App (setzt der AppController).</summary>
    public static Func<IReadOnlyList<MachineGroup>>? GroupSource { get; set; }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MachineBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((MachineBox)d).OnTextChanged((string)e.NewValue)));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(MachineBox), new PropertyMetadata("Rechnernamen oder @Gruppe, mit Komma getrennt", (d, e) => ((MachineBox)d)._input.Tag = e.NewValue));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    private readonly TextBox _input;
    private readonly Button _button;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly DockPanel _root;
    private bool _syncing;

    public MachineBox()
    {
        Focusable = false;
        _input = new TextBox { Tag = Placeholder };
        _input.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont");
        _input.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            _syncing = true;
            Text = _input.Text;
            _syncing = false;
            ShowSuggestions();
        };
        _input.PreviewKeyDown += Input_PreviewKeyDown;
        _input.LostKeyboardFocus += (_, e) => { if (e.NewFocus is not ListBoxItem) _popup!.IsOpen = false; };

        _list = new ListBox { MaxHeight = 220, MinWidth = 220 };
        _list.PreviewMouseLeftButtonUp += (_, _) => Pick();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Tab) { Pick(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _popup!.IsOpen = false; _input.Focus(); e.Handled = true; }
        };
        var border = new Border { Child = _list, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(4) };
        border.SetResourceReference(Border.BackgroundProperty, "Bg.Menu");
        border.SetResourceReference(Border.BorderBrushProperty, "Border.Control");
        _popup = new Popup { Child = border, PlacementTarget = _input, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };

        // Fluent-Schaltfläche: „Btn.Standard“ ist in allen Fenstern ein Stil für Wpf.Ui.Controls.Button
        _button = new Wpf.Ui.Controls.Button { Content = L.T("Gruppe einfügen ▾"), Margin = new Thickness(6, 0, 0, 0), ToolTip = L.T("Rechnergruppe einfügen (wird beim Start aufgelöst)") };
        _button.SetResourceReference(StyleProperty, "Btn.Standard");
        _button.Click += (_, _) => OpenGroupMenu();

        _root = new DockPanel();
        DockPanel.SetDock(_button, Dock.Right);
        _root.Children.Add(_button);
        _root.Children.Add(_input);
        _root.Children.Add(_popup);
        AddVisualChild(_root);
        AddLogicalChild(_root);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _root;
    protected override Size MeasureOverride(Size constraint) { _root.Measure(constraint); return _root.DesiredSize; }
    protected override Size ArrangeOverride(Size arrangeBounds) { _root.Arrange(new Rect(arrangeBounds)); return arrangeBounds; }

    private void OnTextChanged(string text)
    {
        if (_syncing) return;
        _syncing = true;
        _input.Text = text ?? string.Empty;
        _syncing = false;
    }

    private static IReadOnlyList<MachineGroup> Groups => GroupSource?.Invoke() ?? Array.Empty<MachineGroup>();

    private static string Label(MachineGroup g) => L.F("@{0}  ·  {1} Rechner", g.Name, g.Machines.Count);

    private void OpenGroupMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _button, Placement = PlacementMode.Bottom };
        if (Groups.Count == 0) menu.Items.Add(new MenuItem { Header = L.T("Noch keine Rechnergruppen (Einstellungen › Rechnergruppen)"), IsEnabled = false });
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
        var text = _input.Text;
        var caret = _input.CaretIndex;
        int start = caret;
        while (start > 0 && !",; \r\n\t".Contains(text[start - 1])) start--;
        return (start, text[start..caret]);
    }

    private void ShowSuggestions()
    {
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
        if (!_popup.IsOpen) return;
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
        if (_list.SelectedItem is ListBoxItem { Tag: string name }) Insert("@" + name, replaceToken: true);
        _popup.IsOpen = false;
    }

    private void Insert(string value, bool replaceToken)
    {
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
