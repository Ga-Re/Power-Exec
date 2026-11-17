using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PowerExec.Desktop.Infrastructure;

/// <summary>
/// Farbfelder als Schaltflächen in einem Menüeintrag (Zeile „Farben des Designs“): Ein Klick soll das Menü schließen wie
/// ein gewöhnlicher Eintrag. Schaltflächen im Kopf eines Menüeintrags tun das nicht von selbst – das übernimmt
/// <c>CloseMenuOnClick</c> und schließt das ganze Kontextmenü samt Untermenüs.
/// </summary>
public static class MenuSwatch
{
    public static readonly DependencyProperty CloseMenuOnClickProperty = DependencyProperty.RegisterAttached(
        "CloseMenuOnClick", typeof(bool), typeof(MenuSwatch), new PropertyMetadata(false, OnChanged));

    public static bool GetCloseMenuOnClick(DependencyObject d) => (bool)d.GetValue(CloseMenuOnClickProperty);
    public static void SetCloseMenuOnClick(DependencyObject d, bool value) => d.SetValue(CloseMenuOnClickProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;
        button.Click -= OnClick;
        if (e.NewValue is true) button.Click += OnClick;
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        // Vom Feld zum Menüeintrag (im Popup des Untermenüs), dann über die Container nach oben bis zum Kontextmenü
        DependencyObject? node = (DependencyObject)sender;
        while (node != null && node is not MenuItem) node = VisualTreeHelper.GetParent(node);
        ItemsControl? owner = node as MenuItem;
        while (owner != null && owner is not ContextMenu)
            owner = ItemsControl.ItemsControlFromItemContainer(owner) ?? (owner as MenuItem)?.Parent as ItemsControl;
        if (owner is ContextMenu menu) menu.IsOpen = false;
    }
}
