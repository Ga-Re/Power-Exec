using PowerExec.Core.Localization;
using System.Windows;

namespace PowerExec.Views;

/// <summary>Kleine Eingabe (z. B. Name einer neuen Gruppe).</summary>
public partial class InputWindow : Window
{
    private Func<string, string?>? _validate;

    public InputWindow()
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    /// <param name="validate">Liefert eine Fehlermeldung oder null, wenn der Wert passt.</param>
    /// <returns>Eingegebener Text oder null bei Abbruch.</returns>
    public static string? Ask(string heading, string prompt, string initial = "", string okText = "OK", Func<string, string?>? validate = null)
    {
        var w = new InputWindow { _validate = validate };
        w.Heading.Text = heading;
        w.Prompt.Text = prompt;
        w.Prompt.Visibility = string.IsNullOrEmpty(prompt) ? Visibility.Collapsed : Visibility.Visible;
        w.ValueBox.Text = initial;
        w.OkButton.Content = okText;
        return w.ShowDialog() == true ? w.ValueBox.Text.Trim() : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var value = ValueBox.Text.Trim();
        var error = value.Length == 0 ? L.T("Please enter a name.") : _validate?.Invoke(value);
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
