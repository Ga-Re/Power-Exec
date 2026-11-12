using System.Windows;
using System.Windows.Controls;

namespace PowerExec.Desktop.Controls;

public enum InfoBarSeverity { Info, Success, Warning, Error }

/// <summary>
/// Rückmeldung wie die InfoBar in Windows 11: farbiger Streifen mit Symbol und Text (grün = erledigt, gelb = Hinweis,
/// rot = Fehler, blau = Info). Ohne Text unsichtbar – Statusmeldungen schieben das Layout nicht als lose Zeile nach unten.
/// Aussehen: Vorlage in <c>Themes/Controls.xaml</c>.
/// </summary>
public sealed class InfoBar : Control
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(InfoBar),
        new PropertyMetadata(null, (d, _) => ((InfoBar)d).Update()));
    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar),
        new PropertyMetadata(InfoBarSeverity.Info));

    static InfoBar() => DefaultStyleKeyProperty.OverrideMetadata(typeof(InfoBar), new FrameworkPropertyMetadata(typeof(InfoBar)));

    public InfoBar() => Update();

    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public InfoBarSeverity Severity { get => (InfoBarSeverity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }

    private void Update()
    {
        Visibility = string.IsNullOrWhiteSpace(Message) ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(this, Message ?? string.Empty);
    }
}
