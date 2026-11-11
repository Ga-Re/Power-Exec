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

/// <content>Suchfeld mit Vorschlägen und die Tastatur in der Liste.</content>
public partial class PanelWindow
{
    /// <summary>„?“ im leeren Suchfeld zeigt die Übersicht der Tastenkürzel statt danach zu suchen (unabhängig vom Tastaturlayout).</summary>
    private void SearchBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == "?" && SearchBox.Text.Length == 0)
        {
            Vm.ShowShortcuts = !Vm.ShowShortcuts;
            e.Handled = true;
        }
    }

    /// <summary>Das geteilte Menü aller Einträge dieses Fensters (für den Selbsttest).</summary>
    internal ContextMenu ItemMenu => (ContextMenu)FindResource("ItemMenu");

    /// <summary>Kontextmenü eines Eintrags per Tastatur öffnen (erster Menüpunkt hat den Fokus).</summary>
    internal bool OpenItemMenu(ItemViewModel item)
    {
        var container = FindContainer(ListScroll, item) ?? FindContainer(this, item);
        var button = container == null ? null : FindDescendant<Button>(container, b => b.ContextMenu != null);
        if (button?.ContextMenu is not { } menu) return false;
        OpenMenuBelow(menu, button, horizontalOffset: 40);
        menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.IsVisible && m.IsEnabled) is { } first) first.Focus();
        });
        return true;
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool> match) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && match(t)) return t;
            var found = FindDescendant(child, match);
            if (found != null) return found;
        }
        return null;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Vm.UpdateSuggestions(SearchBox.Text, SearchBox.CaretIndex);
    private void SearchBox_SelectionChanged(object sender, RoutedEventArgs e) { if (SearchBox.IsKeyboardFocused) Vm.UpdateSuggestions(SearchBox.Text, SearchBox.CaretIndex); }
    private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Vm.CloseSuggestions();

    /// <summary>Klick ins Suchfeld: Filterarten anbieten (nach dem Setzen des Cursors, daher verzögert).</summary>
    private void SearchBox_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (SearchBox.SelectionLength > 0) return; // Text markiert: nicht dazwischenfunken
        Dispatcher.BeginInvoke(() =>
        {
            if (SearchBox.IsKeyboardFocused && SearchBox.SelectionLength == 0) Vm.ShowFilterCategories(SearchBox.Text, SearchBox.CaretIndex);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SuggestionList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(SuggestionList, d) is ListBoxItem { DataContext: Core.Search.SearchSuggestion s })
        {
            Vm.SuggestionIndex = Vm.Suggestions.ToList().IndexOf(s);
            AcceptSuggestion();
            e.Handled = true;
        }
    }

    /// <summary>Gewählten Vorschlag ins Suchfeld übernehmen (ersetzt das Wort vor dem Cursor).</summary>
    private void AcceptSuggestion()
    {
        if (Vm.SelectedSuggestion is not { } s) return;
        var (text, caret) = Core.Search.SearchSuggestions.Apply(SearchBox.Text, SearchBox.CaretIndex, s);
        SearchBox.Text = text;
        SearchBox.CaretIndex = caret;
        SearchBox.Focus();
        Vm.UpdateSuggestions(text, caret); // nach „typ:“ gleich die Werte zeigen
    }

    private void UndoToast_MouseEnter(object sender, MouseEventArgs e) => Vm.PauseUndoToast(true);
    private void UndoToast_MouseLeave(object sender, MouseEventArgs e) => Vm.PauseUndoToast(false);

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ScrollIntoView(Vm.MoveSelection(1));
                e.Handled = true;
                break;
            case Key.Up:
                ScrollIntoView(Vm.MoveSelection(-1));
                e.Handled = true;
                break;
            case Key.Enter:
                Vm.RunSelectedOrFirst();
                e.Handled = true;
                break;
        }
    }

    private void ScrollIntoView(object? item)
    {
        if (item == null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var element = FindContainer(ListScroll, item);
            element?.BringIntoView();
        });
    }

    private static FrameworkElement? FindContainer(DependencyObject parent, object item)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ContentPresenter cp && ReferenceEquals(cp.Content, item)) return cp;
            var found = FindContainer(child, item);
            if (found != null) return found;
        }
        return null;
    }
}
