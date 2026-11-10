using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Core.Localization;

namespace PowerExec.Views;

/// <summary>Gruppe wählen: vorhandene aus der Liste oder eine neue anlegen.</summary>
public partial class GroupPickerWindow : Window
{
    public GroupPickerWindow()
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
    }

    /// <returns>Gewählte bzw. neue Gruppe oder null bei Abbruch.</returns>
    public static string? Pick(string heading, string prompt, IReadOnlyList<string> groups, string suggestion, Window? owner)
    {
        var w = new GroupPickerWindow();
        if (owner is { IsVisible: true }) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.Heading.Text = heading;
        w.Prompt.Text = prompt;
        w.Prompt.Visibility = string.IsNullOrEmpty(prompt) ? Visibility.Collapsed : Visibility.Visible;
        w.GroupBox.ItemsSource = groups;
        w.NewBox.Text = suggestion;
        var match = groups.FirstOrDefault(g => g.Equals(GroupManager.Normalize(suggestion), StringComparison.CurrentCultureIgnoreCase));
        if (match != null) { w.GroupBox.SelectedItem = match; w.ExistingOption.IsChecked = true; }
        else
        {
            if (groups.Count > 0) w.GroupBox.SelectedIndex = 0;
            w.NewOption.IsChecked = true;
            w.Loaded += (_, _) => { w.NewBox.Focus(); w.NewBox.SelectAll(); };
        }
        w.ExistingOption.IsEnabled = groups.Count > 0;
        return w.ShowDialog() == true ? w._result : null;
    }

    private string? _result;

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (GroupBox == null || NewBox == null) return;
        GroupBox.IsEnabled = ExistingOption.IsChecked == true;
        NewBox.IsEnabled = NewOption.IsChecked == true;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void GroupBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (GroupBox.SelectedItem != null && IsLoaded) ExistingOption.IsChecked = true;
    }

    private void NewBox_GotFocus(object sender, RoutedEventArgs e) => NewOption.IsChecked = true;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var value = ExistingOption.IsChecked == true ? GroupBox.SelectedItem as string ?? string.Empty : GroupManager.Normalize(NewBox.Text);
        if (value.Length == 0)
        {
            ErrorText.Text = ExistingOption.IsChecked == true ? L.T("Please choose a group.") : L.T("Please enter a group.");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        _result = value;
        DialogResult = true;
    }
}
