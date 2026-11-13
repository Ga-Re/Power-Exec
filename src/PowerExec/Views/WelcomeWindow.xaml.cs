using System.Windows;
using System.Windows.Input;
using PowerExec.Desktop.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow(WelcomeViewModel vm, Action<bool>? closed = null)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        DataContext = vm;
        ViewModel = vm;
        vm.CloseRequested += tryPanel =>
        {
            Close();
            closed?.Invoke(tryPanel);
        };
    }

    public WelcomeViewModel ViewModel { get; }

    /// <summary>Tastenkombination aufnehmen (wie in den Einstellungen); Entf/Rücktaste ohne Zusatztaste entfernt sie.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift) return; // weiter zum nächsten Feld
        e.Handled = true;
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            ViewModel.Hotkey = string.Empty;
            return;
        }
        if (HotkeyService.Format(Keyboard.Modifiers, key) is { } text) ViewModel.Hotkey = text;
    }
}
