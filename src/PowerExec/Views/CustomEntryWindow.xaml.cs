using System.Windows;
using PowerExec.Core.Config;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>Ergebnis des Eintrags-Editors.</summary>
public sealed record CustomEntryEditResult(CustomEntry Entry, string Group, IReadOnlyList<string> Tags);

/// <summary>Eigenen Eintrag anlegen oder bearbeiten (aus Panel und Einstellungen).</summary>
public partial class CustomEntryWindow : Window
{
    private readonly CustomEntryViewModel _vm;

    private CustomEntryWindow(CustomEntryViewModel vm, bool isNew)
    {
        _vm = vm;
        InitializeComponent();
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        Heading.Text = isNew ? "Neuer Eintrag" : "Eintrag bearbeiten";
        Loaded += (_, _) => TargetBox.Focus();
    }

    internal static Window ForTest(CustomEntry entry, IReadOnlyList<string> groups) =>
        new CustomEntryWindow(new CustomEntryViewModel(entry, groups.FirstOrDefault(), new[] { "test" }, groups), isNew: false);

    /// <returns>Der geänderte Eintrag samt Gruppe und Schlagworten oder null bei Abbruch.</returns>
    public static CustomEntryEditResult? Edit(IReadOnlyList<string> groups, CustomEntry entry, bool isNew, string? group, IEnumerable<string> tags, Window? owner = null)
    {
        var vm = new CustomEntryViewModel(entry, group, tags, groups);
        var w = new CustomEntryWindow(vm, isNew);
        if (owner != null) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        return w.ShowDialog() == true ? new CustomEntryEditResult(vm.ToEntry(), GroupManager.Normalize(vm.Group), vm.TagList) : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Validate() is { } error)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
