using PowerExec.Core.Localization;
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
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        Heading.Text = title;
        Fields.ItemsSource = fields;
    }

    internal static Window ForTest(IReadOnlyList<Placeholder> placeholders) =>
        new PlaceholderWindow("Test", placeholders.Select(p => new Field { Name = p.Name, Value = p.Default, History = new[] { "a", "b" } }).ToList());

    /// <summary>Für den Selbsttest: mit Profilauswahl („Nach Profil fragen“).</summary>
    internal static Window ForTestWithProfiles()
    {
        var w = new PlaceholderWindow("Test", new List<Field> { new() { Name = "Ticket", Value = "4711" } });
        var profiles = new[] { new LaunchProfile { Name = "Standard", IsDefault = true }, new LaunchProfile { Name = "Server", RunAsAdmin = true, Machines = { "SRV1", "SRV2" } } };
        w.ShowProfiles(profiles, profiles[0]);
        return w;
    }

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

    /// <summary>
    /// „Nach Profil fragen“: Profil wählen, dazu die Platzhalter des Eintrags (falls vorhanden) – alles in einem Fenster.
    /// </summary>
    /// <returns>Gewähltes Profil und Werte, null bei Abbruch.</returns>
    public static (LaunchProfile Profile, Dictionary<string, string> Values)? AskWithProfile(string title, IReadOnlyList<Placeholder> placeholders,
        Func<string, IReadOnlyList<string>> history, IReadOnlyList<LaunchProfile> profiles, LaunchProfile selected)
    {
        var fields = placeholders.Select(p =>
        {
            var h = history(p.Name);
            return new Field { Name = p.Name, History = h, Value = h.FirstOrDefault() ?? p.Default };
        }).ToList();
        var w = new PlaceholderWindow(title, fields);
        w.ShowProfiles(profiles, selected);
        if (w.ShowDialog() != true || w.ProfileBox.SelectedItem is not LaunchProfile chosen) return null;
        return (chosen, fields.ToDictionary(f => f.Name, f => f.Value.Trim(), StringComparer.CurrentCultureIgnoreCase));
    }

    private void ShowProfiles(IReadOnlyList<LaunchProfile> profiles, LaunchProfile selected)
    {
        ProfileRow.Visibility = Visibility.Visible;
        SubHeading.Text = _fields.Count > 0 ? L.T("Profil wählen und Werte für diesen Start eingeben.") : L.T("Mit welchem Profil starten?");
        ProfileBox.ItemsSource = profiles;
        ProfileBox.SelectedItem = selected;
        ProfileBox.SelectionChanged += (_, _) => UpdateProfileHint();
        UpdateProfileHint();
        if (_fields.Count == 0) Loaded += (_, _) => ProfileBox.Focus();
    }

    private void UpdateProfileHint()
    {
        var p = ProfileBox.SelectedItem as LaunchProfile;
        var parts = new List<string>();
        if (p == null) { ProfileHint.Text = string.Empty; return; }
        if (p.IsDefault) parts.Add(L.T("Standardprofil"));
        if (p.RunAsAdmin) parts.Add(L.T("als Administrator"));
        if (p.Machines.Count > 0) parts.Add(string.Join(", ", p.Machines.Take(4)) + (p.Machines.Count > 4 ? " …" : string.Empty));
        ProfileHint.Text = string.Join(" · ", parts);
        ProfileHint.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
