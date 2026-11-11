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

/// <content>Tastatur und Menüs des Panels: Tastenkürzel, Kontextmenüs, Doppelklick, Profil-Menü.</content>
public partial class PanelWindow
{
    /// <summary>Tastenkürzel des Panels (am Fenster bzw. bei Glas am Inhalt im Kindfenster).</summary>
    private void OnPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        {
            if (HandleShortcut(e)) e.Handled = true;
            else if (e.Key == Key.Escape)
            {
                if (Vm.ShowGroupJump) { Vm.ShowGroupJump = false; SearchBox.Focus(); }
                else if (Vm.HasMultiSelection && !Vm.ShowPeek && !Vm.ShowHistory) Vm.ClearChecked();
                else if (Vm.ShowPeek) Vm.ClosePeek();
                else if (Vm.ShowHistory) Vm.ShowHistory = false;
                else if (Vm.ShowShortcuts) Vm.ShowShortcuts = false;
                else if (Vm.IsSearching) Vm.SearchText = string.Empty;
                else HidePanel();
                e.Handled = true;
            }
            // Strg+Z: letzte Änderung im Panel zurücknehmen (im Suchfeld mit Text gilt das Rückgängig des Textfelds)
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control &&
                     !(Keyboard.FocusedElement is TextBox { Text.Length: > 0 }) && Vm.TryUndo())
            {
                e.Handled = true;
            }
            // Strg+V außerhalb eines Textfelds bzw. Strg+Umschalt+V: Zwischenablage als neuen Eintrag übernehmen
            else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                     (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.FocusedElement is not TextBox))
            {
                e.Handled = true;
                Vm.AddFromClipboardCommand.Execute(null);
            }
        }
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button) OpenMenuBelow(menu, button);
    }

    /// <summary>
    /// Kontextmenü unter einem Element öffnen (⋯-Knopf, Tastatur). ItemMenu und GroupMenu gibt es nur einmal je Fenster:
    /// Ziel und Lage werden beim Schließen zurückgesetzt, damit der nächste Rechtsklick wieder am Mauszeiger öffnet.
    /// Der DataContext folgt dort per Bindung dem Ziel; nur Menüs ohne diese Bindung bekommen ihn direkt.
    /// </summary>
    private static void OpenMenuBelow(ContextMenu menu, FrameworkElement target, double horizontalOffset = 0)
    {
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.HorizontalOffset = horizontalOffset;
        if (System.Windows.Data.BindingOperations.GetBindingExpression(menu, DataContextProperty) == null)
            menu.DataContext = target.DataContext;
        void Reset(object? s, RoutedEventArgs a)
        {
            menu.Closed -= Reset;
            menu.ClearValue(ContextMenu.PlacementTargetProperty);
            menu.ClearValue(ContextMenu.PlacementProperty);
            menu.ClearValue(ContextMenu.HorizontalOffsetProperty);
        }
        menu.Closed += Reset;
        menu.IsOpen = true;
    }

    /// <summary>Rechtsklick: geteiltes Menü an das Element hängen, dem es gehört (Zeile, Kachel, Gruppenkopf).</summary>
    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { ContextMenu: { } menu } owner)
            {
                if (!ReferenceEquals(menu.PlacementTarget, owner)) menu.PlacementTarget = owner;
                return;
            }
        }
    }

    /// <summary>Doppelklick auf einen Eintrag: wie der Ausführen-Knopf (Standardprofil).</summary>
    private void Row_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is FrameworkElement { DataContext: ItemViewModel item })
        {
            e.Handled = true;
            item.RunCommand.Execute(null);
        }
    }

    /// <summary>Pfeil am Ausführen-Knopf: Menü mit allen Startprofilen.</summary>
    private void ProfileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ItemViewModel item } button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var p in item.ProfileMenu)
        {
            var header = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
            if (p.IsDefault)
                title.Children.Add(new TextBlock { Text = L.T("Default"), FontSize = 11, Margin = new Thickness(8, 1, 0, 0), Foreground = (Brush)FindResource("Text.Tertiary") });
            if (p.IsAdmin)
                title.Children.Add(new TextBlock { Text = "\uE7EF", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12, Margin = new Thickness(8, 1, 0, 0), Foreground = (Brush)FindResource("Status.Warning") });
            header.Children.Add(title);
            header.Children.Add(new TextBlock { Text = p.Summary, FontSize = 11, Foreground = (Brush)FindResource("Text.Secondary") });
            var mi = new MenuItem { Header = header, Command = p.Command };
            mi.Height = double.NaN;
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = L.T("Manage profiles …"), Command = item.ManageProfilesCommand });
        menu.IsOpen = true;
    }

    /// <summary>
    /// Tastenkürzel im Panel (Übersicht mit F1 oder „?“). Gelten im Suchfeld und auf Einträgen,
    /// nicht in anderen Textfeldern.
    /// </summary>
    private bool HandleShortcut(KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var inSearch = ReferenceEquals(Keyboard.FocusedElement, SearchBox);
        if (Keyboard.FocusedElement is TextBox && !inSearch) return false;

        // Strg+Leertaste im Suchfeld: Filterarten anbieten (wie ein Klick ins Feld)
        if (inSearch && key == Key.Space && mods == ModifierKeys.Control) { Vm.ShowFilterCategories(SearchBox.Text, SearchBox.CaretIndex); return true; }

        // Vorschläge unter dem Suchfeld: Tab übernimmt; bei „#…“/„typ:…“ auch ↑ ↓ und Enter
        if (inSearch && Vm.HasSuggestions)
        {
            if (key == Key.Tab && mods == ModifierKeys.None) { AcceptSuggestion(); return true; }
            if (key == Key.Escape) { Vm.CloseSuggestions(); return true; }
            if (Vm.SuggestionsTakeKeys && mods == ModifierKeys.None)
            {
                if (key == Key.Down) { Vm.SuggestionIndex++; SuggestionList.ScrollIntoView(Vm.SelectedSuggestion); return true; }
                if (key == Key.Up) { Vm.SuggestionIndex--; SuggestionList.ScrollIntoView(Vm.SelectedSuggestion); return true; }
                if (key == Key.Enter) { AcceptSuggestion(); return true; }
            }
        }

        // Alt+1 … 9: Favoriten starten
        if (mods == ModifierKeys.Alt && key is >= Key.D1 and <= Key.D9) return Vm.RunQuickItem(key - Key.D0);
        if (mods == ModifierKeys.Alt && key is >= Key.NumPad1 and <= Key.NumPad9) return Vm.RunQuickItem(key - Key.NumPad0);

        if (key == Key.F1 && mods == ModifierKeys.None) { Vm.ShowShortcuts = !Vm.ShowShortcuts; return true; }
        if (key == Key.K && mods == ModifierKeys.Control) { Vm.ShowGroupJump = !Vm.ShowGroupJump; return true; }
        if (key == Key.H && mods == ModifierKeys.Control) { Vm.ShowHistory = !Vm.ShowHistory; return true; }

        // Leertaste: Vorschau (im Suchfeld nur, solange es leer ist – sonst ist es ein Leerzeichen)
        if (key == Key.Space && mods == ModifierKeys.None)
        {
            var onItem = Keyboard.FocusedElement is FrameworkElement { DataContext: ItemViewModel };
            if (Vm.ShowPeek || onItem || (inSearch && SearchBox.Text.Length == 0)) return Vm.TogglePeek();
        }
        if (Vm.ShowPeek && mods == ModifierKeys.None && key is Key.Up or Key.Down)
        {
            Vm.MovePeek(key == Key.Down ? 1 : -1);
            return true;
        }

        var item = Vm.CurrentItem;
        if (item == null) return false;
        bool Exec(ICommand command)
        {
            if (!command.CanExecute(null)) return false;
            command.Execute(null);
            return true;
        }
        switch (key)
        {
            case Key.Enter when mods == (ModifierKeys.Control | ModifierKeys.Shift): return Exec(item.RunAsUserCommand);
            case Key.Enter when mods == ModifierKeys.Control: return Exec(item.RunAdminCommand);
            case Key.R when mods == ModifierKeys.Control: return Exec(item.RunRemoteCommand);
            case Key.E when mods == ModifierKeys.Control: return Exec(item.EditCommand);
            case Key.D when mods == ModifierKeys.Control: return Exec(item.ToggleFavoriteCommand);
            case Key.C when mods == (ModifierKeys.Control | ModifierKeys.Shift): return Exec(item.CopyPathCommand);
            case Key.F2 when mods == ModifierKeys.None: return Exec(item.ManageProfilesCommand);
            // → am Ende des Suchtexts oder Tab: Aktionen des Eintrags als Menü
            case Key.Right when mods == ModifierKeys.None && (!inSearch || SearchBox.CaretIndex == SearchBox.Text.Length) && Vm.SelectedItem != null:
            case Key.Tab when mods == ModifierKeys.None && inSearch && Vm.SelectedItem != null:
                return OpenItemMenu(Vm.SelectedItem!);
        }
        return false;
    }
}
