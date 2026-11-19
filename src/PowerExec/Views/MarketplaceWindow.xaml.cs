using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerExec.Desktop.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

/// <summary>
/// Der Marktplatz als eigenes Fenster: Navigation links (Bereiche, Kategorien), rechts die Galerie der Karten bzw. die
/// Detailseite eines Eintrags. Der Wechsel zwischen beiden blendet weich über (<see cref="NavigationMotion.SwapPages"/>).
/// </summary>
public partial class MarketplaceWindow : Window
{
    private readonly MarketplaceWindowViewModel _vm;
    private MarketplaceViewModel? _page;

    public MarketplaceWindow(MarketplaceWindowViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        WindowHelper.ApplyTitleBar(this);
        FluentTheme.Attach(this);
        QuietScrollBar.Attach(PageScroll);
        QuietScrollBar.Attach(NavScroll);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarketplaceWindowViewModel.Tabs)) _tabs.Clear(); // neue Navigation: die alten Reiter sind weg
            if (e.PropertyName == nameof(MarketplaceWindowViewModel.Tab)) SyncTabs();
            if (e.PropertyName == nameof(MarketplaceWindowViewModel.Page)) HookPage();
        };
        HookPage();
        // Zurück zur Galerie wie im Browser: Maustaste „Zurück“ bzw. Alt+←
        MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.XButton1 || !_vm.Page.IsDetailOpen) return;
            _vm.Page.CloseDetail();
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (!_vm.Page.IsDetailOpen || !(e.Key == Key.BrowserBack || (e.Key == Key.System && e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt))) return;
            _vm.Page.CloseDetail();
            e.Handled = true;
        };
        // Ausprobiertes Design ohne „Übernehmen“: beim Schließen zurücknehmen
        Closing += (_, _) => vm.Closing();
    }

    /// <summary>Für den Selbsttest: das ViewModel des Fensters.</summary>
    internal MarketplaceWindowViewModel ViewModel => _vm;

    /// <summary>Für den Selbsttest: die Detailseite.</summary>
    internal MarketplaceDetailView DetailForTest => Detail;

    /// <summary>Für den Selbsttest: ist gerade die Galerie (true) bzw. die Detailseite zu sehen?</summary>
    internal bool IsGalleryShownForTest => PageScroll.Visibility == Visibility.Visible && Detail.Visibility != Visibility.Visible;

    /// <summary>Listen und Befehle wurden ersetzt (Einstellungen anderswo geändert): neu anhängen, Seite ohne Bewegung zeigen.</summary>
    private void HookPage()
    {
        if (_page != null) _page.PropertyChanged -= Page_PropertyChanged;
        _page = _vm.Page;
        _page.PropertyChanged += Page_PropertyChanged;
        ShowDetail(_page.IsDetailOpen, animate: false);
    }

    private void Page_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarketplaceViewModel.IsDetailOpen) && sender is MarketplaceViewModel page) ShowDetail(page.IsDetailOpen, animate: true);
    }

    /// <summary>Galerie ↔ Detailseite: vorwärts von rechts, zurück von links; die Karten der Detailseite blenden gestaffelt ein.</summary>
    private void ShowDetail(bool open, bool animate)
    {
        FrameworkElement from = open ? PageScroll : Detail, to = open ? Detail : PageScroll;
        if (!animate)
        {
            from.Visibility = Visibility.Collapsed;
            to.Visibility = Visibility.Visible;
            return;
        }
        if (open) Detail.ScrollToTop();
        NavigationMotion.SwapPages(from, to, forward: open);
        if (open) NavigationMotion.Stagger(Detail.Sections);
    }

    private readonly List<RadioButton> _tabs = new();

    private void Tab_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton tab) return;
        if (!_tabs.Contains(tab)) _tabs.Add(tab);
        tab.IsChecked = (string?)tab.Tag == _vm.Tab;
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key } && key != _vm.Tab) _vm.Tab = key;
    }

    /// <summary>Klick auf den schon gewählten Bereich, während eine Detailseite offen ist: zurück zur Galerie.</summary>
    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key } && key == _vm.Tab) _vm.Page.CloseDetail();
    }

    /// <summary>Bereich von außen gewählt (Verweis aus den Einstellungen): den passenden Reiter markieren.</summary>
    private void SyncTabs()
    {
        foreach (var tab in _tabs) tab.IsChecked = (string?)tab.Tag == _vm.Tab;
    }
}
