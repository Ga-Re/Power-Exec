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

    private void InsertPlaceholder_Click(object sender, RoutedEventArgs e) => InsertAt(UrlBox, "{{Name}}");

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
