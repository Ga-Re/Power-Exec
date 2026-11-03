using System.Windows;
using System.Windows.Controls;
using PowerExec.Core.Config;

namespace PowerExec.Views;

/// <summary>Fragt die Platzhalter eines eigenen Eintrags ab ({{Name}}), mit Verlauf.</summary>
public partial class PlaceholderWindow : Window
{
    public sealed class Field
    {
        public required string Name { get; init; }
        public string Value { get; set; } = string.Empty;
        public IReadOnlyList<string> History { get; init; } = Array.Empty<string>();
    }

    private readonly List<Field> _fields;
    private bool _focused;

    private PlaceholderWindow(string title, List<Field> fields)
    {
        _fields = fields;
        InitializeComponent();
        WindowHelper.ApplyTitleBar(this);
        Heading.Text = title;
        Fields.ItemsSource = fields;
    }

    internal static Window ForTest(IReadOnlyList<Placeholder> placeholders) =>
        new PlaceholderWindow("Test", placeholders.Select(p => new Field { Name = p.Name, Value = p.Default, History = new[] { "a", "b" } }).ToList());

    public static Dictionary<string, string>? Ask(string title, IReadOnlyList<Placeholder> placeholders, Func<string, IReadOnlyList<string>> history)
    {
        var fields = placeholders.Select(p =>
        {
            var h = history(p.Name);
            return new Field { Name = p.Name, History = h, Value = h.FirstOrDefault() ?? p.Default };
        }).ToList();
        var w = new PlaceholderWindow(title, fields);
        return w.ShowDialog() == true ? fields.ToDictionary(f => f.Name, f => f.Value.Trim(), StringComparer.CurrentCultureIgnoreCase) : null;
    }

    private void Field_Loaded(object sender, RoutedEventArgs e)
    {
        if (_focused || sender is not ComboBox box) return;
        _focused = true;
        box.Focus();
        if (box.Template.FindName("PART_EditableTextBox", box) is TextBox tb) tb.SelectAll();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
