using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using PowerExec.ViewModels;

namespace PowerExec.Views;

/// <summary>Eine eigene Variable bearbeiten (Name, Art, Wert, Werte je Arbeitsbereich, PowerShell-Befehl …).</summary>
public partial class VariableWindow : Window
{
    private VariableWindow(VariableRowViewModel row)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        DataContext = row;
        Heading.Text = row.IsNew ? L.T("Neue Variable") : L.T("Variable bearbeiten");
    }

    /// <returns>true = übernehmen, false = abgebrochen.</returns>
    public static bool Edit(VariableRowViewModel row, Window? owner)
    {
        var w = new VariableWindow(row);
        if (owner != null && owner.IsVisible) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // offene Eingaben (Verzögerung der Bindung) übernehmen
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (System.Windows.Input.Keyboard.FocusedElement is PasswordBox pb) SecretBox_LostFocus(pb, e);
        if (DataContext is VariableRowViewModel { CanSave: true }) DialogResult = true;
    }

    /// <summary>Geheimer Wert: beim Verlassen verschlüsselt übernehmen, das Feld wird wieder leer.</summary>
    private void SecretBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box || box.Password.Length == 0 || box.DataContext is not VariableRowViewModel row) return;
        row.SecretValue = box.Password;
        box.Clear();
    }

    internal static Window ForTest(VariableRowViewModel row) => new VariableWindow(row);
}
