using System.Windows;
using System.Windows.Controls;
using PowerExec.Desktop.Infrastructure;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

/// <summary>Der Marktplatz als eigenes Fenster: Reiter für Einträge, Scripte, Designs, Installiert und Quellen.</summary>
public partial class MarketplaceWindow : Window
{
    private readonly MarketplaceWindowViewModel _vm;

    public MarketplaceWindow(MarketplaceWindowViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        WindowHelper.ApplyTitleBar(this);
        FluentTheme.Attach(this);
        QuietScrollBar.Attach(PageScroll);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MarketplaceWindowViewModel.Tab)) SyncTabs(); };
    }

    /// <summary>Für den Selbsttest: das ViewModel des Fensters.</summary>
    internal MarketplaceWindowViewModel ViewModel => _vm;

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

    /// <summary>Bereich von außen gewählt (Verweis aus den Einstellungen): den passenden Reiter markieren.</summary>
    private void SyncTabs()
    {
        foreach (var tab in _tabs) tab.IsChecked = (string?)tab.Tag == _vm.Tab;
    }
}
