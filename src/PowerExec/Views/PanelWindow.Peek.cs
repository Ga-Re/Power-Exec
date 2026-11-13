using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Desktop.Infrastructure;
using PowerExec.Desktop.Interop;
using PowerExec.UI.Services;
using PowerExec.Desktop.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

/// <content>Vorschau (Leertaste) im eigenen Fenster neben dem Panel.</content>
public partial class PanelWindow
{
    // ---- Vorschau im eigenen Fenster ----------------------------------------------------------

    private PeekWindow? _peekWindow;

    /// <summary>Vorschau-Karte aus dem Panel lösen: sie erscheint in einem eigenen Fenster neben dem Panel.</summary>
    private void DetachPeekCard()
    {
        if (PeekCard.Parent is Panel parent) parent.Children.Remove(PeekCard);
        PeekCard.Margin = new Thickness(12);
        PeekCloseButton.Command = Vm.ClosePeekCommand;
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PanelViewModel.ShowPeek) or nameof(PanelViewModel.Peek)) UpdatePeekWindow();
            else if (e.PropertyName == nameof(PanelViewModel.IsPinned) && !Cfg.Pinned) WatchCursor(); // gelöst: wieder auf Verlassen achten
        };
    }

    /// <summary>
    /// Vorschaufenster zeigen, ausblenden und mit dem aktuellen Eintrag füllen. Der Datenkontext wird direkt gesetzt:
    /// eine Bindung der Karte an „Peek“ zieht nach dem Umzug ins eigene Fenster nicht zuverlässig nach.
    /// </summary>
    private void UpdatePeekWindow()
    {
        PeekCard.DataContext = Vm.Peek;
        if (Vm.ShowPeek && IsOpen && !_hiding && _monitor != null)
        {
            _peekWindow ??= new PeekWindow(PeekCard, Resources, this) { DataContext = Vm };
            _peekWindow.PlaceBeside(_rect, _monitor.WorkArea, _monitor.Scale, Cfg.Edge);
            if (!_peekWindow.IsVisible) _peekWindow.Show();
            ScreenService.Place(new WindowInteropHelper(_peekWindow).Handle, _peekWindow.PixelRect);
        }
        else _peekWindow?.Hide();
    }

    /// <summary>Für den Selbsttest: ist das Vorschaufenster sichtbar?</summary>
    internal bool PeekWindowVisible => _peekWindow?.IsVisible == true;
}
