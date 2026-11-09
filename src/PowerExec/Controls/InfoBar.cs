using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PowerExec.Controls;

public enum InfoBarSeverity { Info, Success, Warning, Error }

/// <summary>
/// Rückmeldung wie die InfoBar in Windows 11: farbiger Streifen mit Symbol und Text (grün = erledigt, gelb = Hinweis,
/// rot = Fehler, blau = Info). Ohne Text unsichtbar – Statusmeldungen schieben das Layout nicht als lose Zeile nach unten.
/// </summary>
public sealed class InfoBar : Border
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(InfoBar),
        new PropertyMetadata(null, (d, _) => ((InfoBar)d).Update()));
    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar),
        new PropertyMetadata(InfoBarSeverity.Info, (d, _) => ((InfoBar)d).Update()));

    private readonly TextBlock _icon = new() { FontSize = 14, Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

    public InfoBar()
    {
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 9, 12, 9);
        Margin = new Thickness(0, 6, 0, 4);
        _icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var panel = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Left);
        panel.Children.Add(_icon);
        panel.Children.Add(_text);
        Child = panel;
        Update();
    }

    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public InfoBarSeverity Severity { get => (InfoBarSeverity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }

    private void Update()
    {
        Visibility = string.IsNullOrWhiteSpace(Message) ? Visibility.Collapsed : Visibility.Visible;
        _text.Text = Message ?? string.Empty;
        var (glyph, key) = Severity switch
        {
            InfoBarSeverity.Success => ("", "InfoBar.Success"),
            InfoBarSeverity.Warning => ("", "InfoBar.Warning"),
            InfoBarSeverity.Error => ("", "InfoBar.Error"),
            _ => ("", "InfoBar.Info")
        };
        _icon.Text = glyph;
        SetResourceReference(BackgroundProperty, key + ".Bg");
        SetResourceReference(BorderBrushProperty, key + ".Border");
        _icon.SetResourceReference(TextBlock.ForegroundProperty, key + ".Icon");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
        System.Windows.Automation.AutomationProperties.SetName(this, Message ?? string.Empty);
    }
}
