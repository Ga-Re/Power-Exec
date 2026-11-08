using System.Windows;
using System.Windows.Controls;
using PowerExec.Core.Config;

namespace PowerExec.Views;

/// <summary>Vorlagen-Katalog mit Suche und Mehrfachauswahl.</summary>
public partial class CatalogWindow : Window
{
    public sealed class Row
    {
        public required CatalogTemplate Template { get; init; }
        public string Header { get; set; } = string.Empty;
        public bool IsChecked { get; set; }
    }

    private readonly List<Row> _rows = ToolCatalog.All.Select(t => new Row { Template = t }).ToList();

    private CatalogWindow()
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        Filter();
        Loaded += (_, _) => SearchBox.Focus();
    }

    internal static Window ForTest() => new CatalogWindow();

    public static IReadOnlyList<CatalogTemplate>? Pick()
    {
        var w = new CatalogWindow();
        return w.ShowDialog() == true ? w._rows.Where(r => r.IsChecked).Select(r => r.Template).ToList() : null;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => Filter();

    private void Filter()
    {
        var words = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var visible = _rows.Where(r => words.All(w => r.Template.SearchText.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
        string? last = null;
        foreach (var r in visible)
        {
            r.Header = r.Template.Category != last ? r.Template.Category : string.Empty;
            last = r.Template.Category;
        }
        List.ItemsSource = null;
        List.ItemsSource = visible;
        UpdateCount();
    }

    private void Check_Changed(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        var n = _rows.Count(r => r.IsChecked);
        CountText.Text = n == 0 ? "Nichts ausgewählt" : n == 1 ? "1 Vorlage ausgewählt" : $"{n} Vorlagen ausgewählt";
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
