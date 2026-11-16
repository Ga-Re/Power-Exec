using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

/// <summary>Felder eines eigenen Eintrags je Art; DataContext ist ein <see cref="CustomEntryViewModel"/>.</summary>
public partial class CustomEntryForm : UserControl
{
    public CustomEntryForm()
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        Loaded += (_, _) => AttachVariableCompletion();
    }

    private static readonly HashSet<string> VariableFields = new() { "Target", "Arguments", "Code", "User", "RemoteCommand", "WorkingDirectory" };
    private readonly HashSet<TextBox> _completionAttached = new();

    /// <summary>Vorschläge bei {{$ in allen Feldern, in denen Variablen eingesetzt werden.</summary>
    private void AttachVariableCompletion()
    {
        void Walk(DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is TextBox box && !_completionAttached.Contains(box) &&
                    box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path is { } path && VariableFields.Contains(path))
                {
                    _completionAttached.Add(box);
                    Infrastructure.VariableCompletion.Attach(box, () => Vm?.Builtins ?? Array.Empty<string>());
                }
                Walk(child);
            }
        }
        Walk(this);
    }

    private CustomEntryViewModel? Vm => DataContext as CustomEntryViewModel;

    /// <summary>Text an der Cursorposition einfügen.</summary>
    private static void InsertAt(TextBox box, string text)
    {
        var pos = box.SelectionStart;
        box.SelectedText = text;
        box.SelectionStart = pos + text.Length;
        box.SelectionLength = 0;
        box.Focus();
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void InsertIntoCode_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string text || Vm == null) return;
        InsertAt(Vm.IsTextSnippet ? TextCodeBox : PsCodeBox, text);
    }

    /// <summary>
    /// Knopf {{…}}: Auswahl mit Suchfeld – Abfragen beim Start, eingebaute und eigene Variablen samt Beschreibung. Ein
    /// Klick oder Enter fügt an der Cursorposition ein; bei einer Abfrage ist danach der Name markiert.
    /// </summary>
    private void InsertPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var view = new ListCollectionView(Vm.VariableChoices.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VariableChoice.Section)));
        VariableList.ItemsSource = view;
        VariableSearch.Text = string.Empty;
        VariableEmpty.Visibility = Visibility.Collapsed;
        VariablePopup.IsOpen = true;
        Dispatcher.BeginInvoke(() => VariableSearch.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void VariableSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (VariableList.ItemsSource is not ListCollectionView view) return;
        var words = VariableSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        view.Filter = o => o is VariableChoice c && words.All(w =>
            (c.Token + " " + c.Description + " " + c.Section).Contains(w, StringComparison.CurrentCultureIgnoreCase));
        VariableEmpty.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (!view.IsEmpty) VariableList.SelectedIndex = 0;
    }

    private void VariableSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when VariableList.Items.Count > 0:
                if (VariableList.SelectedIndex < 0) VariableList.SelectedIndex = 0;
                (VariableList.ItemContainerGenerator.ContainerFromIndex(VariableList.SelectedIndex) as UIElement)?.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                if (VariableList.SelectedItem == null && VariableList.Items.Count > 0) VariableList.SelectedIndex = 0;
                PickVariable();
                e.Handled = true;
                break;
            case Key.Escape:
                VariablePopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void VariableList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { PickVariable(); e.Handled = true; }
        else if (e.Key == Key.Escape) { VariablePopup.IsOpen = false; e.Handled = true; }
    }

    private void VariableList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Klick auf einen Eintrag (nicht auf Bereichsüberschrift oder Bildlaufleiste)
        if (ItemsControl.ContainerFromElement(VariableList, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: VariableChoice } item)
        {
            VariableList.SelectedItem = item.DataContext;
            PickVariable();
        }
    }

    private void PickVariable()
    {
        if (VariableList.SelectedItem is not VariableChoice choice) return;
        VariablePopup.IsOpen = false;
        var text = choice.Token;
        var start = UrlBox.SelectionStart;
        InsertAt(UrlBox, text);
        if (!text.StartsWith("{{$", StringComparison.Ordinal))
        {
            // Abfrage: den Namen (ohne Vorgabe) markieren, damit man ihn gleich überschreibt
            var nameLength = text.IndexOf(':') is > 0 and var colon ? colon - 2 : text.Length - 4;
            UrlBox.Select(start + 2, nameLength);
        }
    }

    /// <summary>Zeilennummern mit dem Code mitscrollen.</summary>
    private void CodeBox_ScrollChanged(object sender, ScrollChangedEventArgs e) => LineScroll.ScrollToVerticalOffset(e.VerticalOffset);

    private void ToolList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToolList.SelectedItem is ToolChoice t && Vm != null)
        {
            Vm.PickToolCommand.Execute(t);
            ToolList.SelectedItem = null;
        }
    }
}
