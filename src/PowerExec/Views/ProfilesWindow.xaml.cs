using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Core.Models;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>Einstellungen eines Eintrags: eigener Eintrag (Art, Ziel …), Anzeige, Gruppe, Schlagworte, Notiz und Startprofile.</summary>
public partial class ProfilesWindow : Window
{
    private readonly ProfilesViewModel _vm;

    public ProfilesWindow(ProfilesViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        vm.CloseRequested = () =>
        {
            try { DialogResult = vm.SavedKey != null; }
            catch (InvalidOperationException) { Close(); } // nicht modal geöffnet
        };
    }

    /// <summary>Eigenen Eintrag modal anlegen oder bearbeiten.</summary>
    /// <returns>Schlüssel des gespeicherten Eintrags oder null bei Abbruch.</returns>
    public static string? EditEntry(AppController app, AppSettings target, CustomEntry entry, bool isNew, bool saveImmediately,
        string? group = null, IEnumerable<string>? tags = null, Window? owner = null)
    {
        LaunchItem item = entry.ToLaunchItem();
        var vm = new ProfilesViewModel(app, item, target, saveImmediately, entry, isNew, group, tags);
        var w = new ProfilesWindow(vm);
        if (owner != null) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        return w.ShowDialog() == true ? vm.SavedKey : null;
    }

    internal static Window ForTest(AppController app, CustomEntry entry) =>
        new ProfilesWindow(new ProfilesViewModel(app, entry.ToLaunchItem(), app.Settings, false, entry, isNew: true));

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
