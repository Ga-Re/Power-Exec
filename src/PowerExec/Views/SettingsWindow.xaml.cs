using System.Windows;
using System.Windows.Input;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly AppController _app;

    public SettingsWindow(SettingsViewModel vm, AppController app)
    {
        _vm = vm;
        _app = app;
        InitializeComponent();
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        Closed += (_, _) => vm.Detach();
        vm.ReopenRequested += () =>
        {
            Close();
            _app.ShowSettings();
        };
        vm.SwitchWorkspaceRequested += name =>
        {
            FocusManager.SetFocusedElement(this, this);
            _app.ApplySettings(_vm.Build());
            Close();
            _app.SwitchWorkspace(name);
            _app.ShowSettings(section: "workspaces");
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Fokus wegnehmen, damit Bindungen mit LostFocus übernommen werden
        FocusManager.SetFocusedElement(this, this);
        var hotkeyError = _app.ApplySettings(_vm.Build());
        if (hotkeyError != null)
        {
            _vm.HotkeyError = hotkeyError;
            _vm.Section = "panels";
            SaveHint.Text = "Gespeichert – aber das Tastenkürzel konnte nicht registriert werden.";
            return;
        }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>„Neu ▾“: Menü mit den Eintragsarten öffnen.</summary>
    private void NewEntryMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.DataContext = button.DataContext;
            menu.IsOpen = true;
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_vm.SelectedPanel != null) _vm.SelectedPanel.Hotkey = string.Empty;
            return;
        }
        var text = HotkeyService.Format(Keyboard.Modifiers, key);
        if (text != null)
        {
            if (_vm.SelectedPanel != null) _vm.SelectedPanel.Hotkey = text;
            _vm.HotkeyError = null;
        }
    }

}
