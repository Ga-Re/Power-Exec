using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using PowerExec.Core.Config;
using PowerExec.Desktop.Services;
using PowerExec.Desktop.Interop;
using PowerExec.Desktop.Infrastructure;

namespace PowerExec.Desktop.Controls;

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

    /// <summary>Welche Variante gezeigt wird: dunkel (true), hell (false) oder – null – wie das Design bzw. Windows.</summary>
    public static readonly DependencyProperty ShowDarkProperty = DependencyProperty.Register(nameof(ShowDark), typeof(bool?), typeof(DesignPreview),
        new PropertyMetadata(null, (d, _) => ((DesignPreview)d).Rebuild()));

    public bool? ShowDark { get => (bool?)GetValue(ShowDarkProperty); set => SetValue(ShowDarkProperty, value); }

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

    /// <summary>Hintergrundbild von Windows (verkleinert); null, wenn es keins gibt (dann Farbverlauf).</summary>
    private static ImageSource? Wallpaper
    {
        get
        {
            if (_wallpaperLoaded) return _wallpaper;
            _wallpaperLoaded = true;
            try
            {
                var path = NativeMethods.DesktopWallpaperPath();
                if (File.Exists(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path);
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
        var dark = ShowDark ?? d.Theme switch { ThemeMode.Light => false, ThemeMode.Dark => true, _ => winDark };
        // Die gezeigte Variante: das Paar, wenn es die andere Helligkeit ist; ohne Paar folgt nur ein „wie Windows“-Design
        var usePair = d.Pair != null && dark != d.PrimaryIsDark;
        if (d.Pair == null && d.Theme != ThemeMode.System) dark = d.PrimaryIsDark;
        var colors = usePair ? new DesignColors(d.Pair!.PanelColor, d.Pair.AccentColor, d.Pair.Palette) : new DesignColors(d.PanelColor, d.AccentColor, d.Palette);
        var resolved = Appearance.Resolve(colors, dark);
        var groups = Appearance.SplitColors(usePair && Appearance.SplitColors(d.Pair!.GroupColors).Count > 0 ? d.Pair.GroupColors : d.GroupColors);
        var panel = resolved.Surface.ToColor();
        var ink = resolved.Text.ToColor();
        var accent = !d.WindowsAccent && colors.AccentColor.Length > 0 ? resolved.Accent.ToColor()
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
            Background = new SolidColorBrush(winBg),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 10, 0, 0),
            Width = 64,
            Height = 42,
            Opacity = 0.97,
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
            Height = 9,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(winDark ? Color.FromArgb(0xD0, 0x1C, 0x1C, 0x1C) : Color.FromArgb(0xD0, 0xEE, 0xEE, 0xEE))
        });

        // Glas: Desktop hinter dem Panel weichgezeichnet
        if (d.Backdrop == PanelBackdrop.Glass && Wallpaper is { } wp2)
        {
            _blurred = new Image { Source = wp2, Stretch = Stretch.UniformToFill, Effect = new BlurEffect { Radius = 8 } };
            _root.Children.Add(_blurred);
        }

        // Panel rechts bündig
        var sub = resolved.Card.ToColor();
        var input = resolved.Input.ToColor();
        var line = resolved.Line.ToColor();
        var selectedColor = resolved.Selected.ToColor();
        var radius = CornerRadii.Of(d.Corners ?? CornerStyle.Standard).Card / 3;
        var content = new StackPanel { Margin = new Thickness(6, 5, 6, 0) };
        var title = new TextBlock { Text = "Power-Exec", FontSize = 7.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink) };
        if (!string.IsNullOrEmpty(d.FontFamily)) title.FontFamily = new FontFamily(d.FontFamily + ", Segoe UI");
        content.Children.Add(title);
        content.Children.Add(new Border
        {
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Margin = new Thickness(0, 4, 0, 0),
            Background = new SolidColorBrush(input),
            BorderBrush = new SolidColorBrush(line),
            BorderThickness = new Thickness(0.5)
        });
        var selected = new Grid { Height = 7, Margin = new Thickness(0, 4, 0, 0) };
        selected.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = new SolidColorBrush(selectedColor) });
        selected.Children.Add(new Border { Width = 2.5, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(accent) });
        content.Children.Add(selected);
        // Gruppen in den Farben der Reihe (ohne Reihe: Linien in Textfarbe)
        for (int i = 0; i < 2; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, i == 0 ? 10 : 20, 0) };
            var stripe = groups.Count > 0 && PanelColors.TryParse(groups[i % groups.Count], out var g) ? g.ToColor() : Mix(panel, ink, 0.45);
            var mark = new Border { Width = 2.5, Height = 5, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(stripe), Margin = new Thickness(0, 0, 3, 0) };
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            row.Children.Add(new Border { Height = 5, CornerRadius = new CornerRadius(radius), Background = new SolidColorBrush(d.GroupStyle == GroupCardStyle.Tinted ? Mix(panel, stripe, 0.3) : sub) });
            content.Children.Add(row);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        buttons.Children.Add(new Border { Width = 20, Height = 7, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(accent) });
        buttons.Children.Add(new Border { Width = 15, Height = 7, CornerRadius = new CornerRadius(2), Margin = new Thickness(3, 0, 0, 0), Background = new SolidColorBrush(sub) });
        content.Children.Add(buttons);
        _panel = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 11),
            CornerRadius = new CornerRadius(4, 0, 0, 4),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), panel.R, panel.G, panel.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, ink.R, ink.G, ink.B)),
            BorderThickness = new Thickness(0.6, 0.6, 0, 0.6),
            Child = content
        };
        _root.Children.Add(_panel);

        // Hover-Zone: leuchtet am rechten Rand
        _root.Children.Add(new Border
        {
            Width = 2.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(1.5, 0, 0, 1.5),
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
