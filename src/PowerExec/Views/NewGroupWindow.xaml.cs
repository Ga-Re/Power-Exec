using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using PowerExec.Core.Config;

namespace PowerExec.Desktop.Views;

/// <summary>
/// Dialog „Neue Gruppe“: Name, Symbol oder Bild, optional ein Ordner (dann smarte Gruppe), Farbe und Darstellung.
/// Wo die Gruppe entsteht, entscheidet der Aufrufer (Untergruppe bei Rechtsklick auf eine Gruppe, sonst oberste Ebene unten).
/// </summary>
public partial class NewGroupWindow : Window
{
    private Func<string, string?>? _validateName;
    private Func<string?, string?>? _validateFolder;
    private IconRef _icon = IconRef.None;
    private bool _nameTouched, _settingName;
    private readonly List<KeyValuePair<string, string>> _colors = new();

    public NewGroupWindow()
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        _colors.Add(new KeyValuePair<string, string>(string.Empty, L.T("None / like parent")));
        _colors.AddRange(PanelColors.AccentPresets.Select(p => new KeyValuePair<string, string>(p.Hex, p.Name)));
        ColorBox.ItemsSource = _colors;
        ColorBox.SelectedIndex = 0;
        LayoutBox.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("panel", L.T("Like the panel")),
            new KeyValuePair<string, string>("List", L.T("List")),
            new KeyValuePair<string, string>("Grid", L.T("Grid"))
        };
        LayoutBox.SelectedIndex = 0;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    /// <summary>Für den Selbsttest: Fenster ohne Abfrage.</summary>
    internal static Window ForTest() => new NewGroupWindow { Title = "Power-Exec" };

    /// <param name="validateName">Fehlermeldung zum Namen oder null.</param>
    /// <param name="validateFolder">Fehlermeldung zum Ordner (z. B. schon vergeben) oder null.</param>
    /// <returns>Angaben oder null bei Abbruch.</returns>
    /// <param name="initialFolder">Vorausgefüllter Ordner (z. B. ins Panel gezogen) oder null.</param>
    public static NewGroupSpec? Ask(string heading, string initialName, Func<string, string?> validateName, Func<string?, string?>? validateFolder,
        string? initialFolder = null)
    {
        var w = new NewGroupWindow { _validateName = validateName, _validateFolder = validateFolder };
        w.Heading.Text = heading;
        w._settingName = true;
        w.NameBox.Text = initialName;
        w._settingName = false;
        if (!string.IsNullOrWhiteSpace(initialFolder))
        {
            w.FolderBox.Text = initialFolder;
            w.ClearFolderButton.Visibility = Visibility.Visible;
        }
        return w.ShowDialog() == true ? w.Result() : null;
    }

    private NewGroupSpec Result() => new(
        NameBox.Text.Trim(),
        _icon,
        FolderBox.Text.Trim().Length > 0 ? FolderBox.Text.Trim() : null,
        ColorBox.SelectedValue as string ?? string.Empty,
        (LayoutBox.SelectedValue as string) switch { "Grid" => ItemLayout.Grid, "List" => ItemLayout.List, _ => null });

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingName && IsLoaded) _nameTouched = true;
    }

    private void Icon_Click(object sender, RoutedEventArgs e)
    {
        Topmost = false; // Auswahlfenster nicht verdecken
        var picked = IconPickerWindow.Pick(_icon, L.T("Icon or image for the group"));
        Topmost = true;
        if (picked == null) return;
        _icon = picked.Value;
        IconPreview.Icon = _icon.ToString();
        IconPreview.Visibility = _icon.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        IconPlaceholder.Visibility = _icon.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = L.T("Select folder for the group") };
        if (dlg.ShowDialog(this) != true) return;
        FolderBox.Text = dlg.FolderName;
        ClearFolderButton.Visibility = Visibility.Visible;
        // Name aus dem Ordner vorschlagen, solange noch nichts Eigenes eingetippt wurde
        if (!_nameTouched)
        {
            _settingName = true;
            NameBox.Text = NewGroup.NameFromFolder(dlg.FolderName) is { Length: > 0 } n ? n : NameBox.Text;
            _settingName = false;
        }
        ShowError(_validateFolder?.Invoke(FolderBox.Text));
    }

    private void ClearFolder_Click(object sender, RoutedEventArgs e)
    {
        FolderBox.Text = string.Empty;
        ClearFolderButton.Visibility = Visibility.Collapsed;
        ShowError(null);
    }

    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        Topmost = false;
        var hex = ColorPickerWindow.Pick(L.T("Color for the group"), ColorBox.SelectedValue as string, this);
        Topmost = true;
        if (hex == null) return;
        if (!_colors.Any(c => c.Key.Equals(hex, StringComparison.OrdinalIgnoreCase)))
        {
            _colors.Add(new KeyValuePair<string, string>(hex, L.T("Custom ") + hex));
            ColorBox.ItemsSource = null;
            ColorBox.ItemsSource = _colors;
        }
        ColorBox.SelectedValue = _colors.First(c => c.Key.Equals(hex, StringComparison.OrdinalIgnoreCase)).Key;
    }

    private void ShowError(string? error)
    {
        ErrorText.Text = error ?? string.Empty;
        ErrorText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var error = name.Length == 0 ? L.T("Please enter a name.") : _validateName?.Invoke(name);
        error ??= _validateFolder?.Invoke(FolderBox.Text.Trim().Length > 0 ? FolderBox.Text.Trim() : null);
        if (error != null)
        {
            ShowError(error);
            return;
        }
        DialogResult = true;
    }
}
