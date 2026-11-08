using System.Windows;
using System.Windows.Input;
using PowerExec.Core.Execution;
using PowerExec.Services;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>Schwebende Suche in der Bildschirmmitte (Spotlight-Modus); schließt bei Esc oder Klick daneben.</summary>
public partial class SpotlightWindow : Window
{
    private bool _closing;

    public SpotlightWindow(SpotlightViewModel vm, MonitorInfo monitor)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyContentScale((FrameworkElement)Content);
        ViewModel = vm;
        DataContext = vm;
        vm.CloseRequested += SafeClose;
        Deactivated += (_, _) => { if (CloseOnDeactivate) SafeClose(); };
        Loaded += (_, _) =>
        {
            // Mittig im oberen Drittel des Monitors mit dem Mauszeiger (WPF-Einheiten)
            var scale = monitor.Scale <= 0 ? 1 : monitor.Scale;
            var wa = monitor.WorkArea;
            Left = wa.Left / scale + (wa.Width / scale - ActualWidth) / 2;
            Top = wa.Top / scale + wa.Height / scale * 0.18;
            Activate();
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        };
    }

    public SpotlightViewModel ViewModel { get; }

    /// <summary>Klick daneben schließt (der Selbsttest schaltet das ab).</summary>
    public bool CloseOnDeactivate { get; set; } = true;

    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.Escape: SafeClose(); break;
            case Key.Down: ViewModel.SelectedIndex++; break;
            case Key.Up: ViewModel.SelectedIndex--; break;
            case Key.Enter when mods == (ModifierKeys.Control | ModifierKeys.Shift): ViewModel.Run(ExecutionMode.OtherUser); break;
            case Key.Enter when mods == ModifierKeys.Control: ViewModel.Run(ExecutionMode.Administrator); break;
            case Key.Enter: ViewModel.Run(); break;
            case Key.R when mods == ModifierKeys.Control: ViewModel.Run(remote: true); break;
            case Key.Tab: ViewModel.OpenPanel(); break;
            default: return;
        }
        e.Handled = true;
    }
}
