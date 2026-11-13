using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerExec.Desktop.Services;

namespace PowerExec.Desktop.Views.Settings;

/// <summary>Einstellungen › Panel › Verhalten.</summary>
public partial class PanelBehaviorPage : UserControl
{
    public PanelBehaviorPage() => InitializeComponent();

    /// <summary>Tastenkürzel aufnehmen: die gedrückte Kombination wird übernommen, Rück- bzw. Entf-Taste leert das Feld.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (Window.GetWindow(this) is not SettingsWindow host) return;
        var vm = host.CurrentViewModel;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            vm.Panel.Hotkey = string.Empty;
            host.ScheduleApply();
            return;
        }
        var text = HotkeyService.Format(Keyboard.Modifiers, key);
        if (text != null)
        {
            vm.Panel.Hotkey = text;
            vm.HotkeyError = null;
            host.ScheduleApply();
        }
    }
}
