using PowerExec.Views;

namespace PowerExec.Services;

/// <summary><see cref="IShell"/> für Windows/WPF.</summary>
public sealed class WpfShell : IShell
{
    public void OpenLocation(string path, bool isFolder = false) => Launcher.OpenLocation(path, isFolder);

    public void OpenUrl(string url) => Launcher.OpenUrl(url);

    public void SetClipboardText(string text)
    {
        try { System.Windows.Clipboard.SetText(text); } catch { /* Zwischenablage belegt */ }
    }

    public bool IsPwshInstalled => Launcher.IsPwshInstalled;
}

/// <summary><see cref="IDialogs"/> mit den WPF-Fenstern; im Selbsttest beantwortet <paramref name="testAnswer"/> die Frage.</summary>
public sealed class WpfDialogs(Func<Func<string?>?> testAnswer) : IDialogs
{
    public string? Ask(string heading, string prompt, string initial = "", string okText = "OK", Func<string, string?>? validate = null) =>
        testAnswer()?.Invoke() ?? InputWindow.Ask(heading, prompt, initial, okText, validate);
}

/// <summary><see cref="IThemeInfo"/> aus dem <see cref="ThemeService"/>.</summary>
public sealed class WpfThemeInfo : IThemeInfo
{
    public bool IsHighContrast => ThemeService.IsHighContrast;
    public bool IsDark => ThemeService.IsDark;
}
