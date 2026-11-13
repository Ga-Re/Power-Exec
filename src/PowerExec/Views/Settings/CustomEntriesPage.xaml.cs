using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PowerExec.Desktop.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views.Settings;

/// <summary>Einstellungen › Inhalte › Eigene Einträge: Liste mit Suche, Filtern und Mehrfachauswahl.</summary>
public partial class CustomEntriesPage : UserControl
{
    public CustomEntriesPage() => InitializeComponent();

    private SettingsWindow? Host => Window.GetWindow(this) as SettingsWindow;

    /// <summary>„Neu ▾“: Menü öffnen (Eintragsarten).</summary>
    private void NewEntryMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.DataContext = button.DataContext;
            menu.IsOpen = true;
        }
    }

    // ---- Eigene Einträge aus der Liste ins Panel ziehen ---------------------------

    private Point _entryDragStart;
    private CustomEntryRowViewModel? _entryDragRow;

    private void EntryRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _entryDragStart = e.GetPosition(this);
        // Nicht beim Klick auf Haken, ⚙ oder Papierkorb
        _entryDragRow = e.OriginalSource is DependencyObject d && VisualTreeSearch.IsInside<ButtonBase>(d) ? null
            : (sender as FrameworkElement)?.DataContext as CustomEntryRowViewModel;
    }

    private void EntryRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _entryDragRow == null) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _entryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _entryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _entryDragRow;
        _entryDragRow = null;
        Host?.ApplyNow(force: true); // Eintrag muss in der App existieren, bevor er im Panel landet
        var data = new DataObject();
        data.SetData(SettingsWindow.EntryDragFormat, row.Entry.Key);
        data.SetData(DataFormats.UnicodeText, row.Name);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move | DragDropEffects.Copy);
    }
}
