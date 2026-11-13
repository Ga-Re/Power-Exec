using PowerExec.Core.Localization;
using System.Windows;
using PowerExec.UI.Services;

namespace PowerExec.Desktop.Views;

/// <summary>Fragt zwischen wenigen Möglichkeiten (große Schaltflächen mit Erklärung), z. B. Ordner als Gruppe oder Link.</summary>
public partial class ChoiceWindow : Window
{
    private int _choice = -1;

    private ChoiceWindow(string heading, string prompt, IReadOnlyList<ChoiceOption> options, string? forAllText)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
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

    internal static Window ForTest() => new ChoiceWindow(L.T("Add folder “Tools”"), L.T("How should the folder appear in the panel?"), ChoiceOption.FolderDrop, L.T("For all dragged folders"));

    /// <returns>Index der gewählten Möglichkeit (−1 = Abbruch) und ob sie für alle weiteren gelten soll.</returns>
    public static (int Choice, bool ForAll) Ask(string heading, string prompt, IReadOnlyList<ChoiceOption> options, string? forAllText = null)
    {
        var w = new ChoiceWindow(heading, prompt, options, forAllText);
        w.ShowDialog();
        return (w._choice, w.ForAll.IsChecked == true);
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChoiceOption o } && Options.ItemsSource is IReadOnlyList<ChoiceOption> list)
        {
            _choice = list.ToList().IndexOf(o);
            DialogResult = true;
        }
    }
}
