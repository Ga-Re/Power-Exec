using System.Windows;

namespace PowerExec.Views;

/// <summary>Fragt zwischen wenigen Möglichkeiten (große Schaltflächen mit Erklärung), z. B. Ordner als Gruppe oder Link.</summary>
public partial class ChoiceWindow : Window
{
    public sealed record Option(string Glyph, string Title, string Description);

    private int _choice = -1;

    private ChoiceWindow(string heading, string prompt, IReadOnlyList<Option> options, string? forAllText)
    {
        InitializeComponent();
        WindowHelper.ApplyTitleBar(this);
        Heading.Text = heading;
        Prompt.Text = prompt;
        Prompt.Visibility = prompt.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Options.ItemsSource = options;
        if (forAllText != null)
        {
            ForAll.Content = forAllText;
            ForAll.Visibility = Visibility.Visible;
        }
    }

    internal static Window ForTest() => new ChoiceWindow("Ordner „Tools“ hinzufügen", "Wie soll der Ordner im Panel erscheinen?", FolderOptions, "Für alle gezogenen Ordner");

    /// <summary>Möglichkeiten beim Ziehen eines Ordners ins Panel.</summary>
    public static readonly IReadOnlyList<Option> FolderOptions = new[]
    {
        new Option("", "Als Gruppe", "Smarte Gruppe: Scripte und Programme im Ordner erscheinen als Einträge und bleiben aktuell."),
        new Option("", "Als Link", "Ein Eintrag, der den Ordner im Explorer öffnet.")
    };

    /// <returns>Index der gewählten Möglichkeit (−1 = Abbruch) und ob sie für alle weiteren gelten soll.</returns>
    public static (int Choice, bool ForAll) Ask(string heading, string prompt, IReadOnlyList<Option> options, string? forAllText = null)
    {
        var w = new ChoiceWindow(heading, prompt, options, forAllText);
        w.ShowDialog();
        return (w._choice, w.ForAll.IsChecked == true);
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Option o } && Options.ItemsSource is IReadOnlyList<Option> list)
        {
            _choice = list.ToList().IndexOf(o);
            DialogResult = true;
        }
    }
}
