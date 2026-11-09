using System.Windows;
using PowerExec.Core.Config;
using PowerExec.Views;

namespace PowerExec.Services;

/// <summary><see cref="IShell"/> für Windows/WPF.</summary>
public sealed class WpfShell(IDialogs dialogs) : IShell
{
    public void OpenLocation(string path, bool isFolder = false) => Launcher.OpenLocation(path, isFolder, dialogs);

    public void OpenUrl(string url) => Launcher.OpenUrl(url);

    public void SetClipboardText(string text)
    {
        try { System.Windows.Clipboard.SetText(text); } catch { /* Zwischenablage belegt */ }
    }

    public void CopyToClipboard(string text, bool asHtml) => ClipboardText.Set(text, asHtml);

    public string GetClipboardText()
    {
        try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
        catch { return string.Empty; }
    }

    public IReadOnlyList<string> GetClipboardFiles()
    {
        try { return System.Windows.Clipboard.ContainsFileDropList() ? System.Windows.Clipboard.GetFileDropList().Cast<string>().ToList() : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    public bool IsPwshInstalled => Launcher.IsPwshInstalled;
}

/// <summary><see cref="IDialogs"/> mit den WPF-Fenstern; im Selbsttest beantwortet <paramref name="testAnswer"/> die Textfrage.</summary>
public sealed class WpfDialogs(Func<Func<string?>?> testAnswer) : IDialogs
{
    public string? Ask(string heading, string prompt, string initial = "", string okText = "OK", Func<string, string?>? validate = null) =>
        testAnswer()?.Invoke() ?? InputWindow.Ask(heading, prompt, initial, okText, validate);

    private static MessageBoxResult Show(object? owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        owner is Window { IsLoaded: true } w
            ? MessageBox.Show(w, message, title, buttons, image, defaultResult)
            : MessageBox.Show(message, title, buttons, image, defaultResult);

    public void Inform(string message, string title, object? owner = null) =>
        Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warn(string message, string title, object? owner = null) =>
        Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string title, bool destructive = false, object? owner = null, bool defaultNo = false) =>
        Show(owner, message, title, MessageBoxButton.YesNo,
            destructive ? MessageBoxImage.Warning : MessageBoxImage.Question,
            destructive || defaultNo ? MessageBoxResult.No : MessageBoxResult.None) == MessageBoxResult.Yes;

    public bool? ConfirmOrCancel(string message, string title, object? owner = null) =>
        Show(owner, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null
        };

    public string? OpenFile(string filter, string? title = null, object? owner = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        if (title != null) dlg.Title = title;
        return dlg.ShowDialog(owner as Window) == true ? dlg.FileName : null;
    }

    public IReadOnlyList<string> OpenFiles(string filter, string? title = null, object? owner = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter, Multiselect = true };
        if (title != null) dlg.Title = title;
        return dlg.ShowDialog(owner as Window) == true ? dlg.FileNames : Array.Empty<string>();
    }

    public string? SaveFile(string filter, string fileName, string? title = null, object? owner = null)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = fileName };
        if (title != null) dlg.Title = title;
        return dlg.ShowDialog(owner as Window) == true ? dlg.FileName : null;
    }

    public string? PickOrganizationalUnit(IReadOnlyList<OuNode> roots, string? current, object? owner = null) =>
        OuPickerWindow.Pick(roots, current, owner as Window);

    public string? PickFolder(string? title = null, object? owner = null)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (title != null) dlg.Title = title;
        return dlg.ShowDialog(owner as Window) == true ? dlg.FolderName : null;
    }

    public string? PickColor(string title, string? initial, object? owner = null) =>
        ColorPickerWindow.Pick(title, initial, owner as Window);

    public IconRef? PickIcon(IconRef current, string heading) => IconPickerWindow.Pick(current, heading);

    public NewGroupSpec? AskNewGroup(string heading, string initialName, Func<string, string?> validateName, Func<string?, string?>? validateFolder, string? initialFolder = null) =>
        NewGroupWindow.Ask(heading, initialName, validateName, validateFolder, initialFolder);

    public IReadOnlyList<CatalogTemplate>? PickCatalogTemplates() => CatalogWindow.Pick();

    public IReadOnlyDictionary<string, string>? AskPlaceholders(string title, IReadOnlyList<Placeholder> placeholders, Func<string, IReadOnlyList<string>> history) =>
        PlaceholderWindow.Ask(title, placeholders, history);

    public (LaunchProfile Profile, IReadOnlyDictionary<string, string> Values)? AskProfile(string title, IReadOnlyList<Placeholder> placeholders,
        Func<string, IReadOnlyList<string>> history, IReadOnlyList<LaunchProfile> profiles, LaunchProfile selected) =>
        PlaceholderWindow.AskWithProfile(title, placeholders, history, profiles, selected) is { } a ? (a.Profile, a.Values) : null;

    public (int Choice, bool ForAll) Choose(string heading, string prompt, IReadOnlyList<ChoiceOption> options, string? forAllText = null) =>
        ChoiceWindow.Ask(heading, prompt, options, forAllText);
}

/// <summary><see cref="IThemeInfo"/> aus dem <see cref="ThemeService"/>.</summary>
public sealed class WpfThemeInfo : IThemeInfo
{
    public bool IsHighContrast => ThemeService.IsHighContrast;
    public bool IsDark => ThemeService.IsDark;
    public bool TransparencyEffects => ThemeService.TransparencyEffects();
}
