using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using PowerExec.Core.Config;
using PowerExec.Services;

namespace PowerExec.Controls;

/// <summary>
/// Vorschau eines Designs als kleine Desktop-Szene: echtes Windows-Hintergrundbild, ein Fenster im Windows-Design
/// (hell/dunkel), Taskleiste und rechts bündig das Panel in Panelfarbe, Deckkraft und Hintergrundart – mit Suchfeld,
/// ausgewählter Zeile (Akzentstreifen), Textzeilen, Knöpfen und der leuchtenden Hover-Zone am rechten Rand.
/// Wird unter Darstellung (Designvorlagen) und im Marktplatz verwendet.
/// </summary>
public sealed class DesignPreview : Border
{
    public static readonly DependencyProperty DesignProperty = DependencyProperty.Register(nameof(Design), typeof(DesignPreset), typeof(DesignPreview),
        new PropertyMetadata(null, (d, _) => ((DesignPreview)d).Rebuild()));

    public DesignPreset? Design { get => (DesignPreset?)GetValue(DesignProperty); set => SetValue(DesignProperty, value); }

    private readonly Grid _root = new() { ClipToBounds = true };
    private Border? _panel;
    private Image? _blurred;

    public DesignPreview()
    {
        CornerRadius = new CornerRadius(5);
        Child = _root;
        SizeChanged += (_, _) => { UpdateClip(); UpdateBlurClip(); };
        Loaded += (_, _) => Rebuild();
    }

    // ---- Hintergrundbild des Desktops (einmal geladen, verkleinert) ---------------------------------

    private static ImageSource? _wallpaper;
    private static bool _wallpaperLoaded;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);

    /// <summary>Hintergrundbild von Windows (verkleinert); null, wenn es keins gibt (dann Farbverlauf).</summary>
    private static ImageSource? Wallpaper
    {
        get
        {
            if (_wallpaperLoaded) return _wallpaper;
            _wallpaperLoaded = true;
            try
            {
                var sb = new StringBuilder(520);
                if (SystemParametersInfo(0x0073 /* SPI_GETDESKWALLPAPER */, (uint)sb.Capacity, sb, 0) && File.Exists(sb.ToString()))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(sb.ToString());
                    bmp.DecodePixelWidth = 480;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _wallpaper = bmp;
                }
            }
            catch { _wallpaper = null; }
            return _wallpaper;
        }
    }

    private static Brush FallbackDesktop()
    {
        var b = new LinearGradientBrush(Color.FromRgb(0x1E, 0x3C, 0x72), Color.FromRgb(0x8E, 0x44, 0xAD), 35);
        b.Freeze();
        return b;
    }

    // ---- Aufbau ---------------------------------------------------------------------------------

    private void Rebuild()
    {
        _root.Children.Clear();
        _panel = null;
        _blurred = null;
        if (Design is not { } d) return;

        var winDark = ThemeService.SystemUsesDarkTheme();
        var dark = d.Theme switch { ThemeMode.Light => false, ThemeMode.Dark => true, _ => winDark };
        var panel = PanelColors.TryParse(d.PanelColor, out var pc) ? Color.FromRgb(pc.R, pc.G, pc.B)
            : dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        var light = (0.299 * panel.R + 0.587 * panel.G + 0.114 * panel.B) > 150;
        var ink = light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White;
        var accent = PanelColors.TryParse(d.AccentColor, out var ac) ? Color.FromRgb(ac.R, ac.G, ac.B)
            : TryFindResource("Accent") is SolidColorBrush ab ? ab.Color : Color.FromRgb(0x00, 0x78, 0xD4);
        var opacity = d.Backdrop == PanelBackdrop.Solid ? 1.0 : Math.Clamp(d.Opacity / 100.0, 0.2, 1.0);

        // Desktop
        if (Wallpaper is { } wp) _root.Children.Add(new Image { Source = wp, Stretch = Stretch.UniformToFill });
        else _root.Children.Add(new Border { Background = FallbackDesktop() });

        // Fenster im Windows-Design (hell/dunkel)
        var winBg = winDark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White;
        var winBar = winDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xE9, 0xE9, 0xE9);
        var window = new Border
        {
            Background = new SolidColorBrush(winBg), CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 10, 0, 0), Width = 64, Height = 42, Opacity = 0.97,
            Child = new StackPanel
            {
                Children =
                {
                    new Border { Height = 7, Background = new SolidColorBrush(winBar), CornerRadius = new CornerRadius(3, 3, 0, 0) },
                    Line(winDark ? Color.FromArgb(0x55, 255, 255, 255) : Color.FromArgb(0x33, 0, 0, 0), 40, new Thickness(6, 6, 0, 0)),
                    Line(winDark ? Color.FromArgb(0x35, 255, 255, 255) : Color.FromArgb(0x22, 0, 0, 0), 28, new Thickness(6, 4, 0, 0))
                }
            }
        };
        _root.Children.Add(window);

        // Taskleiste
        _root.Children.Add(new Border
        {
            Height = 9, VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(winDark ? Color.FromArgb(0xD0, 0x1C, 0x1C, 0x1C) : Color.FromArgb(0xD0, 0xEE, 0xEE, 0xEE))
        });

        // Glas: Desktop hinter dem Panel weichgezeichnet
        if (d.Backdrop == PanelBackdrop.Glass && Wallpaper is { } wp2)
        {
            _blurred = new Image { Source = wp2, Stretch = Stretch.UniformToFill, Effect = new BlurEffect { Radius = 8 } };
            _root.Children.Add(_blurred);
        }

        // Panel rechts bündig
        var sub = Mix(panel, ink, 0.16);
        var line = Mix(panel, ink, 0.45);
        var content = new StackPanel { Margin = new Thickness(6, 5, 6, 0) };
        content.Children.Add(new TextBlock { Text = "Power-Exec", FontSize = 7.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink) });
        content.Children.Add(new Border { Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 4, 0, 0), Background = new SolidColorBrush(sub) });
        var selected = new Grid { Height = 7, Margin = new Thickness(0, 4, 0, 0) };
        selected.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(sub) });
        selected.Children.Add(new Border { Width = 2.5, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(accent) });
        content.Children.Add(selected);
        content.Children.Add(Line(line, double.NaN, new Thickness(0, 4, 12, 0), 0.9));
        content.Children.Add(Line(line, double.NaN, new Thickness(0, 3, 24, 0), 0.55));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        buttons.Children.Add(new Border { Width = 20, Height = 7, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(accent) });
        buttons.Children.Add(new Border { Width = 15, Height = 7, CornerRadius = new CornerRadius(2), Margin = new Thickness(3, 0, 0, 0), Background = new SolidColorBrush(sub) });
        content.Children.Add(buttons);
        _panel = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 11),
            CornerRadius = new CornerRadius(4, 0, 0, 4), ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), panel.R, panel.G, panel.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, ink.R, ink.G, ink.B)), BorderThickness = new Thickness(0.6, 0.6, 0, 0.6),
            Child = content
        };
        _root.Children.Add(_panel);

        // Hover-Zone: leuchtet am rechten Rand
        _root.Children.Add(new Border
        {
            Width = 2.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(1.5, 0, 0, 1.5),
            Effect = new DropShadowEffect { Color = accent, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.9 }
        });
        UpdateLayoutParts();
        UpdateClip();
    }

    private static Border Line(Color c, double width, Thickness margin, double opacity = 1) =>
        new() { Height = 3, Width = width, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Margin = margin, Opacity = opacity, Background = new SolidColorBrush(c) };

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private void UpdateLayoutParts()
    {
        if (_panel != null && ActualWidth > 0) _panel.Width = Math.Round(ActualWidth * 0.54);
        // Hover-Zone: mittleres Drittel der Höhe
        if (_root.Children.Count > 0 && _root.Children[^1] is Border zone && ActualHeight > 0) zone.Height = Math.Round(ActualHeight * 0.4);
        UpdateBlurClip();
    }

    private void UpdateClip()
    {
        UpdateLayoutParts();
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        _root.Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius.TopLeft, CornerRadius.TopLeft);
    }

    /// <summary>Weichgezeichnetes Bild nur hinter dem Panel zeigen.</summary>
    private void UpdateBlurClip()
    {
        if (_blurred == null || ActualWidth <= 0 || ActualHeight <= 0) return;
        var w = Math.Round(ActualWidth * 0.54);
        _blurred.Clip = new RectangleGeometry(new Rect(ActualWidth - w, 4, w, Math.Max(0, ActualHeight - 15)), 4, 4);
    }
}
