using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Desktop.Infrastructure;

namespace PowerExec.Desktop.Views;

/// <summary>Farbwähler: Sättigung/Helligkeit-Feld, Farbton-Leiste, Vorlagen und #RRGGBB-Feld.</summary>
public partial class ColorPickerWindow : Window
{
    private double _h, _s = 0.7, _v = 0.85;
    private bool _updating;
    private bool _dragging;

    public ColorPickerWindow(string title, string? initial)
    {
        InitializeComponent();
        Infrastructure.FluentTheme.Attach(this);
        WindowHelper.ApplyTitleBar(this);
        Heading.Text = title;
        Presets.ItemsSource = PanelColors.AccentPresets;
        if (PanelColors.TryParse(initial, out var c)) (_h, _s, _v) = ColorMath.ToHsv(c);
        else (_h, _s, _v) = ColorMath.ToHsv(new Rgb(0x3B, 0x82, 0xF6));
        Loaded += (_, _) => { Refresh(updateText: true); HexBox.Focus(); HexBox.SelectAll(); };
        SvArea.SizeChanged += (_, _) => PlaceThumb();
    }

    /// <summary>Gewählte Farbe als #RRGGBB.</summary>
    public string Hex => ColorMath.FromHsv(_h, _s, _v).Hex;

    /// <summary>Farbe wählen; null bei Abbruch.</summary>
    public static string? Pick(string title, string? initial, Window? owner = null)
    {
        var w = new ColorPickerWindow(title, initial);
        if (owner is { IsLoaded: true }) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        return w.ShowDialog() == true ? w.Hex : null;
    }

    private void Refresh(bool updateText)
    {
        _updating = true;
        var hue = ColorMath.FromHsv(_h, 1, 1);
        HueFill.Fill = hue.ToBrush();
        HueSlider.Value = _h;
        var c = ColorMath.FromHsv(_h, _s, _v);
        Preview.Background = c.ToBrush();
        SvThumb.Stroke = _v > 0.6 && _s < 0.4 ? Brushes.Black : Brushes.White;
        if (updateText) HexBox.Text = c.Hex;
        ErrorText.Visibility = Visibility.Collapsed;
        OkButton.IsEnabled = true;
        PlaceThumb();
        _updating = false;
    }

    private void PlaceThumb()
    {
        Canvas.SetLeft(SvThumb, _s * SvArea.ActualWidth - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _v) * SvArea.ActualHeight - SvThumb.Height / 2);
    }

    private void SetFromPoint(Point p)
    {
        if (SvArea.ActualWidth <= 0 || SvArea.ActualHeight <= 0) return;
        _s = Math.Clamp(p.X / SvArea.ActualWidth, 0, 1);
        _v = Math.Clamp(1 - p.Y / SvArea.ActualHeight, 0, 1);
        Refresh(updateText: true);
    }

    private void Sv_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        SvArea.CaptureMouse();
        SetFromPoint(e.GetPosition(SvArea));
    }

    private void Sv_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) SetFromPoint(e.GetPosition(SvArea));
    }

    private void Sv_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        SvArea.ReleaseMouseCapture();
    }

    private void Hue_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _h = e.NewValue;
        Refresh(updateText: true);
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string hex && PanelColors.TryParse(hex, out var c))
        {
            (_h, _s, _v) = ColorMath.ToHsv(c);
            Refresh(updateText: true);
        }
    }

    private void Hex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (PanelColors.TryParse(HexBox.Text, out var c))
        {
            (_h, _s, _v) = ColorMath.ToHsv(c);
            Refresh(updateText: false);
        }
        else
        {
            ErrorText.Visibility = HexBox.Text.Trim().Length >= 4 ? Visibility.Visible : Visibility.Collapsed;
            OkButton.IsEnabled = false;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>Für den Selbsttest.</summary>
    internal static ColorPickerWindow ForTest() => new("Eigene Farbe – Selbsttest", "#22C55E");
}
