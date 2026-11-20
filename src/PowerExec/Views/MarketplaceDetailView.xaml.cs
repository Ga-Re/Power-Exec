using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PowerExec.Core.Localization;
using PowerExec.Desktop.Infrastructure;
using PowerExec.UI.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

/// <summary>
/// Detailseite eines Marktplatz-Eintrags. Ist sie schmal, wandert die linke Spalte (Vorschau, hell/dunkel) als Block über
/// den Inhalt; wird es für den Kopf eng, stehen die Aktionen unter dem Namen.
/// </summary>
public partial class MarketplaceDetailView : UserControl
{
    /// <summary>Schmaler als das: linke Spalte über dem Inhalt.</summary>
    private const double NarrowWidth = 760;
    /// <summary>Inhalt schmaler als das: Aktionen unter dem Namen statt daneben.</summary>
    private const double StackedHeroWidth = 600;
    /// <summary>Höhe der Vorschau, wenn sie als Block über dem Inhalt steht.</summary>
    private const double CompactPreviewHeight = 330;

    private bool _narrow;

    public MarketplaceDetailView()
    {
        InitializeComponent();
        QuietScrollBar.Attach(ContentScroll);
        SizeChanged += (_, _) => UpdateLayoutMode();
        ContentScroll.SizeChanged += (_, _) => UpdateHero();
    }

    /// <summary>Teile der Seite, die beim Öffnen nacheinander einblenden.</summary>
    internal IReadOnlyList<UIElement> Sections => new UIElement[] { Hero, ColorsCard, GroupsCard, BottomCards, EntryCard, VariablesCard };

    /// <summary>Neue Seite: oben anfangen.</summary>
    internal void ScrollToTop() => ContentScroll.ScrollToTop();

    /// <summary>Für den Selbsttest: steht die linke Spalte gerade über dem Inhalt?</summary>
    internal bool IsNarrowForTest => _narrow;

    private void UpdateLayoutMode()
    {
        var narrow = ActualWidth > 0 && ActualWidth < NarrowWidth;
        if (narrow == _narrow) return;
        _narrow = narrow;
        if (narrow)
        {
            SideHost.Child = null;
            SideSlot.Content = Sidebar;
            // Wie die linke Spalte: nur bei Designs
            SideSlot.SetBinding(VisibilityProperty, new Binding(nameof(MarketplaceItemRow.HasDesign)) { Converter = (IValueConverter)FindResource("Vis") });
            Sidebar.Width = double.NaN;
            Preview.Height = CompactPreviewHeight;
        }
        else
        {
            SideSlot.Content = null;
            BindingOperations.ClearBinding(SideSlot, VisibilityProperty);
            SideSlot.Visibility = Visibility.Collapsed;
            SideHost.Child = Sidebar;
            Sidebar.ClearValue(WidthProperty);
            Preview.ClearValue(HeightProperty);
        }
    }

    /// <summary>Aktionen neben (breit) bzw. unter dem Namen (schmal).</summary>
    private void UpdateHero()
    {
        var stacked = ContentScroll.ActualWidth > 0 && ContentScroll.ActualWidth < StackedHeroWidth;
        Grid.SetRow(Actions, stacked ? 1 : 0);
        Grid.SetColumn(Actions, stacked ? 0 : 1);
        Actions.Margin = stacked ? new Thickness(0, 12, 0, 0) : new Thickness(0, 4, 0, 0);
    }

    /// <summary>Klick auf ein Farbfeld: Hexwert in die Zwischenablage.</summary>
    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DesignSwatch swatch) return;
        try
        {
            Clipboard.SetText(swatch.Hex);
            Snackbar.Show(L.F("{0} copied to the clipboard", swatch.Hex));
        }
        catch (ExternalException)
        {
            // Zwischenablage gerade von einem anderen Programm belegt: dann eben nicht kopiert – kein Grund für einen Fehler
        }
    }
}
