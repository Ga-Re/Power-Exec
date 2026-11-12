using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PowerExec.Core.Localization;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using PowerExec.Desktop.Infrastructure;

namespace PowerExec.Desktop.Services;

/// <summary>Art einer Meldung (Symbol und Farbe).</summary>
public enum MessageKind { Info, Warning, Question, Error }

/// <summary>
/// Meldungen und Rückfragen als Fluent-ContentDialog (WPF UI) statt Windows-MessageBox. Der Dialog erscheint in einem
/// durchsichtigen Fenster über dem auslösenden Fenster (abgedunkelt) bzw. mittig auf dem Bildschirm und blockiert wie
/// eine MessageBox, bis eine Schaltfläche gewählt ist.
/// </summary>
public static class MessageDialog
{
    /// <summary>Selbsttest: statt anzuzeigen sofort mit diesem Ergebnis antworten.</summary>
    internal static Func<ContentDialogResult>? TestAnswer { get; set; }

    /// <summary>Zuletzt angezeigte Meldung (Selbsttest).</summary>
    internal static string? LastMessage { get; private set; }

    public static ContentDialogResult Show(object? owner, string message, string title, MessageKind kind,
        string primary, string? secondary = null, string? close = null,
        ContentDialogButton defaultButton = ContentDialogButton.Primary, bool destructive = false)
    {
        LastMessage = message;
        if (TestAnswer is { } answer) return answer();
        var host = new HostWindow(owner as Window);
        var dialog = new ContentDialog(host.Host)
        {
            Title = title,
            Content = Body(message, kind),
            PrimaryButtonText = primary,
            SecondaryButtonText = secondary ?? string.Empty,
            CloseButtonText = close ?? string.Empty,
            DefaultButton = defaultButton,
            PrimaryButtonAppearance = destructive ? ControlAppearance.Danger : ControlAppearance.Primary,
            DialogMaxWidth = 560
        };
        var result = ContentDialogResult.None;
        host.Loaded += async (_, _) =>
        {
            try { result = await dialog.ShowAsync(); }
            finally { host.Close(); }
        };
        try { host.ShowDialog(); }
        catch (InvalidOperationException) { host.Owner = null; host.ShowDialog(); }
        return result;
    }

    public static void Inform(object? owner, string message, string title) =>
        Show(owner, message, title, MessageKind.Info, L.T("OK"));

    public static void Warn(object? owner, string message, string title) =>
        Show(owner, message, title, MessageKind.Warning, L.T("OK"));

    public static void Error(object? owner, string message, string title) =>
        Show(owner, message, title, MessageKind.Error, L.T("OK"));

    public static bool Confirm(object? owner, string message, string title, bool destructive = false, bool defaultNo = false) =>
        Show(owner, message, title, destructive ? MessageKind.Warning : MessageKind.Question, L.T("Yes"), close: L.T("No"),
            defaultButton: destructive || defaultNo ? ContentDialogButton.Close : ContentDialogButton.Primary, destructive: destructive)
        == ContentDialogResult.Primary;

    public static bool? ConfirmOrCancel(object? owner, string message, string title) =>
        Show(owner, message, title, MessageKind.Question, L.T("Yes"), L.T("No"), L.T("Cancel"), ContentDialogButton.Close) switch
        {
            ContentDialogResult.Primary => true,
            ContentDialogResult.Secondary => false,
            _ => null
        };

    private static FrameworkElement Body(string message, MessageKind kind)
    {
        var (glyph, brush) = kind switch
        {
            MessageKind.Warning => ("", "InfoBar.Warning.Icon"),
            MessageKind.Error => ("", "InfoBar.Error.Icon"),
            MessageKind.Question => ("", "Accent"),
            _ => ("", "Accent")
        };
        var grid = new Grid { MinWidth = 320 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = Tokens.IconCard,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont")
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
        var text = new System.Windows.Controls.TextBox
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false
        };
        text.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Text.Primary");
        Grid.SetColumn(text, 1);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>Durchsichtiges Fenster über dem Besitzer; trägt den ContentDialogHost samt Abdunklung.</summary>
    internal sealed class HostWindow : Window
    {
        public ContentDialogHost Host { get; } = new();

        public HostWindow(Window? owner)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = owner == null;
            ResizeMode = ResizeMode.NoResize;
            Title = "Power-Exec";
            Infrastructure.FluentTheme.Attach(this);
            Content = Host;
            if (owner is { IsVisible: true, ActualWidth: >= 420, ActualHeight: >= 300 } && owner.WindowState != WindowState.Minimized)
            {
                Owner = owner;
                Topmost = owner.Topmost;
                WindowStartupLocation = WindowStartupLocation.Manual;
                var maximized = owner.WindowState == WindowState.Maximized;
                Left = maximized ? SystemParameters.WorkArea.Left : owner.Left;
                Top = maximized ? SystemParameters.WorkArea.Top : owner.Top;
                Width = maximized ? SystemParameters.WorkArea.Width : owner.ActualWidth;
                Height = maximized ? SystemParameters.WorkArea.Height : owner.ActualHeight;
            }
            else
            {
                // Ohne passendes Fenster (Panel, Tray): mittig auf dem Bildschirm, nur der Dialog selbst sichtbar
                if (owner is { IsVisible: true }) Owner = owner;
                Topmost = true;
                Width = 640;
                Height = 480;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Host.Background = Brushes.Transparent;
            }
        }
    }
}
