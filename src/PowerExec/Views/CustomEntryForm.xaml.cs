using System.Windows;
using System.Windows.Controls;
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
    /// Knopf {{…}}: Menü mit Abfragen beim Start, eingebauten und eigenen Variablen samt Beschreibung; ein Klick fügt
    /// an der Cursorposition ein. Bei einer Abfrage ist danach der Name markiert, damit man ihn gleich überschreibt.
    /// </summary>
    private void InsertPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || sender is not FrameworkElement button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 480 };
        string? section = null;
        foreach (var choice in Vm.VariableChoices)
        {
            if (choice.Section != section)
            {
                if (section != null) menu.Items.Add(new Separator());
                section = choice.Section;
                menu.Items.Add(new MenuItem { Header = choice.Section, IsEnabled = false, FontWeight = FontWeights.SemiBold });
            }
            var token = new TextBlock { Text = choice.Token, FontWeight = FontWeights.SemiBold };
            token.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            var description = new TextBlock { Text = choice.Description, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
            description.SetResourceReference(TextBlock.FontSizeProperty, "Font.Caption");
            description.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            var item = new MenuItem { Header = new StackPanel { Children = { token, description }, Margin = new Thickness(0, 2, 0, 2) } };
            System.Windows.Automation.AutomationProperties.SetName(item, choice.Token + " – " + choice.Description);
            var text = choice.Token;
            item.Click += (_, _) =>
            {
                var start = UrlBox.SelectionStart;
                InsertAt(UrlBox, text);
                if (!text.StartsWith("{{$", StringComparison.Ordinal))
                {
                    // Abfrage: den Namen (ohne Vorgabe) markieren
                    var nameLength = text.IndexOf(':') is > 0 and var colon ? colon - 2 : text.Length - 4;
                    UrlBox.Select(start + 2, nameLength);
                }
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
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
