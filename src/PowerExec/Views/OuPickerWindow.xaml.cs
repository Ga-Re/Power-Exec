using System.Windows;
using System.Windows.Controls;
using PowerExec.Core.Config;

namespace PowerExec.Views;

/// <summary>OU-Baum des Active Directory: eine Organisationseinheit für „Ab OU“ wählen (Doppelklick oder Übernehmen).</summary>
public partial class OuPickerWindow : Window
{
    private readonly IReadOnlyList<OuNode> _roots;
    private string? _result;

    private OuPickerWindow(IReadOnlyList<OuNode> roots, string? current)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        _roots = roots;
        Tree.ItemsSource = roots;
        Loaded += (_, _) =>
        {
            // Obere Ebene aufklappen und die bisherige OU markieren
            foreach (var root in roots)
                if (Tree.ItemContainerGenerator.ContainerFromItem(root) is TreeViewItem item) item.IsExpanded = true;
            if (!string.IsNullOrWhiteSpace(current)) Reveal(current.Trim());
            Filter.Focus();
        };
    }

    internal static Window ForTest() => new OuPickerWindow(OuTree.Build(new[]
    {
        "DC=firma,DC=local", "OU=Server,DC=firma,DC=local", "OU=Web,OU=Server,DC=firma,DC=local", "OU=Clients,DC=firma,DC=local",
        "CN=Computers,DC=firma,DC=local"
    }), "OU=Web,OU=Server,DC=firma,DC=local");

    /// <returns>Distinguished Name der gewählten OU oder null bei Abbruch.</returns>
    public static string? Pick(IReadOnlyList<OuNode> roots, string? current, Window? owner)
    {
        var w = new OuPickerWindow(roots, current);
        if (owner != null && owner.IsVisible) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w._result : null;
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var node = Tree.SelectedItem as OuNode;
        Selected.Text = node?.DistinguishedName ?? string.Empty;
        Ok.IsEnabled = node != null;
    }

    private void Tree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Tree.SelectedItem is OuNode && e.OriginalSource is FrameworkElement { DataContext: OuNode }) Ok_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is not OuNode node) return;
        _result = node.DistinguishedName;
        DialogResult = true;
    }

    /// <summary>Filter: nur Zweige mit passenden OUs (Name oder DN enthält den Text); leer = ganzer Baum.</summary>
    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = Filter.Text.Trim();
        if (text.Length == 0) { Tree.ItemsSource = _roots; return; }
        static OuNode? Prune(OuNode n, string t)
        {
            var kids = n.Children.Select(c => Prune(c, t)).OfType<OuNode>().ToList();
            if (kids.Count == 0 && !n.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase)
                && !n.DistinguishedName.Contains(t, StringComparison.OrdinalIgnoreCase)) return null;
            var copy = new OuNode { Name = n.Name, DistinguishedName = n.DistinguishedName };
            copy.Children.AddRange(kids);
            return copy;
        }
        Tree.ItemsSource = _roots.Select(r => Prune(r, text)).OfType<OuNode>().ToList();
        Dispatcher.BeginInvoke(() => ExpandAll(Tree), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void ExpandAll(ItemsControl parent)
    {
        foreach (var item in parent.Items)
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
            {
                tvi.IsExpanded = true;
                tvi.UpdateLayout();
                ExpandAll(tvi);
            }
    }

    /// <summary>Den Weg zur bisherigen OU aufklappen und sie auswählen.</summary>
    private void Reveal(string dn)
    {
        ItemsControl parent = Tree;
        foreach (var node in PathTo(_roots, dn) ?? new List<OuNode>())
        {
            parent.UpdateLayout();
            if (parent.ItemContainerGenerator.ContainerFromItem(node) is not TreeViewItem tvi) return;
            tvi.IsExpanded = true;
            if (node.DistinguishedName.Equals(dn, StringComparison.OrdinalIgnoreCase)) { tvi.IsSelected = true; tvi.BringIntoView(); }
            parent = tvi;
        }
    }

    private static List<OuNode>? PathTo(IEnumerable<OuNode> nodes, string dn)
    {
        foreach (var n in nodes)
        {
            if (n.DistinguishedName.Equals(dn, StringComparison.OrdinalIgnoreCase)) return new List<OuNode> { n };
            if (PathTo(n.Children, dn) is { } rest) { rest.Insert(0, n); return rest; }
        }
        return null;
    }
}
