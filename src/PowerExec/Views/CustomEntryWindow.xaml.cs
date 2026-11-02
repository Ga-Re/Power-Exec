using System.Windows;
using PowerExec.Core.Config;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>Eigenen Eintrag direkt aus dem Panel anlegen oder bearbeiten.</summary>
public partial class CustomEntryWindow : Window
{
    private readonly CustomEntryViewModel _vm;

    private CustomEntryWindow(CustomEntryViewModel vm, bool isNew, string? group)
    {
        _vm = vm;
        InitializeComponent();
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        Heading.Text = isNew ? "Neuer Eintrag" : "Eintrag bearbeiten";
        GroupText.Text = string.IsNullOrEmpty(group) ? "Erscheint unter „Eigene Einträge“." : $"Erscheint in der Gruppe „{group}“.";
        Loaded += (_, _) => TargetBox.Focus();
    }

    /// <returns>Der geänderte Eintrag oder null bei Abbruch.</returns>
    public static CustomEntry? Edit(CustomEntry entry, bool isNew, string? group)
    {
        var w = new CustomEntryWindow(new CustomEntryViewModel(entry, _ => { }), isNew, group);
        return w.ShowDialog() == true ? w._vm.ToEntry() : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Target.Trim().Length == 0)
        {
            ErrorText.Text = _vm.TargetLabel + " fehlt.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
