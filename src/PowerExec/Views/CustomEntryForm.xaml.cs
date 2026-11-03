using System.Windows;
using System.Windows.Controls;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>Felder eines eigenen Eintrags je Art; DataContext ist ein <see cref="CustomEntryViewModel"/>.</summary>
public partial class CustomEntryForm : UserControl
{
    public CustomEntryForm() => InitializeComponent();

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
