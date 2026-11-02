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
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Fokus wegnehmen, damit Bindungen mit LostFocus übernommen werden
        FocusManager.SetFocusedElement(this, this);
        var hotkeyError = _app.ApplySettings(_vm.Build());
        if (hotkeyError != null)
        {
            _vm.HotkeyError = hotkeyError;
            _vm.Section = "panel";
            SaveHint.Text = "Gespeichert – aber das Tastenkürzel konnte nicht registriert werden.";
            return;
        }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            _vm.Hotkey = string.Empty;
            return;
        }
        var text = HotkeyService.Format(Keyboard.Modifiers, key);
        if (text != null)
        {
            _vm.Hotkey = text;
            _vm.HotkeyError = null;
        }
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e) => _vm.Hotkey = string.Empty;
}
