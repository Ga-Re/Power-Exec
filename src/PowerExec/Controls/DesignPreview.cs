using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using PowerExec.Core.Config;
using PowerExec.Core.Localization;
using PowerExec.Desktop.Services;
using PowerExec.Desktop.Interop;
using PowerExec.Desktop.Infrastructure;
using PowerExec.UI.ViewModels;

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

    /// <summary>
    /// Große Vorschau (Detailseite im Marktplatz): statt der Desktop-Szene ein ganzes Panel auf dem Hintergrundbild – Kopf
    /// mit Symbol und Zahnrad, Suchfeld, Gruppen mit Kacheln (eine ausgewählt) und Schlagworten.
    /// </summary>
    public static readonly DependencyProperty DetailedProperty = DependencyProperty.Register(nameof(Detailed), typeof(bool), typeof(DesignPreview),
        new PropertyMetadata(false, (d, _) => ((DesignPreview)d).Rebuild()));

    public bool Detailed { get => (bool)GetValue(DetailedProperty); set => SetValue(DetailedProperty, value); }

    /// <summary>Abstand des Panels zum Rand der Bühne in der großen Vorschau.</summary>
    private const double StageInset = 16;
    private double _panelRadius;

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

        if (Detailed)
        {
            BuildDetailed(d, resolved, accent, groups, opacity);
            UpdateClip();
            return;
        }

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

    // ---- Große Vorschau -------------------------------------------------------------------

    /// <summary>Ganzes Panel auf dem (leicht abgedunkelten) Hintergrundbild: so wirken Deckkraft, Glas und Milchglas wie echt.</summary>
    private void BuildDetailed(DesignPreset d, ResolvedPalette p, Color accent, IReadOnlyList<string> groups, double opacity)
    {
        CornerRadius = Tokens.RadiusTile;
        var panel = p.Surface.ToColor();
        var ink = p.Text.ToColor();
        var secondary = p.TextSecondary.ToColor();
        var card = p.Card.ToColor();
        var radii = CornerRadii.Of(d.Corners ?? CornerStyle.Standard);
        _panelRadius = radii.Tile;
        var iconFont = TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        var onAccent = PanelColors.IsLight(new Rgb(accent.R, accent.G, accent.B)) ? Colors.Black : Colors.White;

        // Bühne
        if (Wallpaper is { } wp) _root.Children.Add(new Image { Source = wp, Stretch = Stretch.UniformToFill });
        else _root.Children.Add(new Border { Background = FallbackDesktop() });
        _root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)) });
        if (d.Backdrop == PanelBackdrop.Glass && Wallpaper is { } wp2)
        {
            _blurred = new Image { Source = wp2, Stretch = Stretch.UniformToFill, Effect = new BlurEffect { Radius = 14 } };
            _root.Children.Add(_blurred);
        }

        TextBlock Text(string text, double size, Color color, bool strong = false) => new()
        {
            Text = text,
            FontSize = size,
            Foreground = new SolidColorBrush(color),
            FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        TextBlock Glyph(string glyph, double size, Color color) => new()
        {
            Text = glyph,
            FontFamily = iconFont,
            FontSize = size,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var content = new StackPanel { Margin = new Thickness(12) };
        if (!string.IsNullOrEmpty(d.FontFamily)) TextElement.SetFontFamily(content, new FontFamily(d.FontFamily + ", Segoe UI"));

        // Kopf: Symbol in Akzentfarbe, Name, Zahnrad
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var icon = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(Math.Max(radii.Control, 3) + 1),
            Background = new SolidColorBrush(accent),
            Child = Glyph("", 11, onAccent)
        };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        var gear = Glyph("", 12, secondary);
        DockPanel.SetDock(gear, Dock.Right);
        header.Children.Add(gear);
        var title = Text("Power-Exec", 12.5, ink, strong: true);
        title.Margin = new Thickness(8, 0, 8, 0);
        header.Children.Add(title);
        content.Children.Add(header);

        // Suchfeld
        var search = new DockPanel();
        var lens = Glyph("", 11, secondary);
        lens.Margin = new Thickness(9, 0, 7, 0);
        DockPanel.SetDock(lens, Dock.Left);
        search.Children.Add(lens);
        search.Children.Add(Text(L.T("Search …"), 11.5, secondary));
        content.Children.Add(new Border
        {
            Height = 28,
            Margin = new Thickness(0, 0, 0, 14),
            CornerRadius = new CornerRadius(radii.Control),
            Background = new SolidColorBrush(p.Input.ToColor()),
            BorderBrush = new SolidColorBrush(p.Line.ToColor()),
            BorderThickness = new Thickness(1),
            Child = search
        });

        // Drei Beispielgruppen in den Farben der Reihe (ohne Reihe: neutral)
        var names = MarketplaceItemRow.SampleGroupNames;
        var tiles = L.T("Cleanup|RDP").Split('|');
        var tags = L.T("#net|#ad").Split('|');
        var tileRadius = new CornerRadius(radii.Card);
        for (int g = 0; g < 3; g++)
        {
            var hasColor = groups.Count > 0 && PanelColors.TryParse(groups[g % groups.Count], out _);
            var color = hasColor && PanelColors.TryParse(groups[g % groups.Count], out var gc) ? gc.ToColor() : Mix(panel, ink, 0.45);
            var tileBg = d.GroupStyle == GroupCardStyle.Tinted ? Mix(panel, color, 0.3) : card;
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var bar = new Border { Width = 3, Height = 12, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(bar, Dock.Left);
            head.Children.Add(bar);
            head.Children.Add(Text(g < names.Count ? names[g] : string.Empty, 11, hasColor ? color : ink, strong: true));
            content.Children.Add(head);

            Border Tile(string label, Color background, Color? frame, double height)
            {
                var inner = new Grid();
                if (d.GroupStyle == GroupCardStyle.Accent)
                    inner.Children.Add(new Border { Width = 2.5, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.25), Background = new SolidColorBrush(color), Margin = new Thickness(0, 6, 0, 6) });
                if (label.Length > 0)
                {
                    var t = Text(label, 11, ink, strong: true);
                    t.Margin = new Thickness(10, 0, 6, 0);
                    inner.Children.Add(t);
                }
                return new Border
                {
                    Height = height,
                    CornerRadius = tileRadius,
                    Background = new SolidColorBrush(background),
                    BorderBrush = new SolidColorBrush(frame ?? Colors.Transparent),
                    BorderThickness = new Thickness(frame == null ? 0 : 1.5),
                    Child = inner
                };
            }

            FrameworkElement body;
            if (g < 2)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                var left = g == 0 ? Tile(tiles[0], tileBg, null, 40) : Tile(string.Empty, tileBg, null, 28);
                var right = g == 0 ? Tile(tiles.Length > 1 ? tiles[1] : string.Empty, p.Selected.ToColor(), accent, 40) : Tile(string.Empty, tileBg, null, 28);
                if (g == 1) left.Opacity = right.Opacity = 0.75;
                Grid.SetColumn(right, 2);
                grid.Children.Add(left);
                grid.Children.Add(right);
                body = grid;
            }
            else
            {
                var wrap = new WrapPanel();
                foreach (var tag in tags)
                    wrap.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(8, 2, 8, 3),
                        Margin = new Thickness(0, 0, 6, 0),
                        Background = new SolidColorBrush(Mix(panel, color, 0.18)),
                        BorderBrush = new SolidColorBrush(Mix(panel, color, 0.45)),
                        BorderThickness = new Thickness(1),
                        Child = Text(tag, 10.5, hasColor ? color : ink)
                    });
                body = wrap;
            }
            body.Margin = new Thickness(0, 0, 0, 14);
            content.Children.Add(body);
        }

        var surface = new Grid();
        if (d.Backdrop != PanelBackdrop.Solid && d.Frost > 0)
            surface.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(d.Frost / 100.0, 0, 1) * 0.35 * 255), 255, 255, 255)) });
        surface.Children.Add(content);
        _panel = new Border
        {
            Margin = new Thickness(StageInset),
            CornerRadius = new CornerRadius(radii.Tile),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), panel.R, panel.G, panel.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, ink.R, ink.G, ink.B)),
            BorderThickness = new Thickness(1),
            Child = surface
        };
        _root.Children.Add(_panel);
    }

    private static Border Line(Color c, double width, Thickness margin, double opacity = 1) =>
        new() { Height = 3, Width = width, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Margin = margin, Opacity = opacity, Background = new SolidColorBrush(c) };

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private void UpdateLayoutParts()
    {
        if (Detailed)
        {
            UpdateBlurClip();
            return;
        }
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
        if (Detailed)
        {
            _blurred.Clip = new RectangleGeometry(new Rect(StageInset, StageInset, Math.Max(0, ActualWidth - 2 * StageInset), Math.Max(0, ActualHeight - 2 * StageInset)),
                _panelRadius, _panelRadius);
            return;
        }
        var w = Math.Round(ActualWidth * 0.54);
        _blurred.Clip = new RectangleGeometry(new Rect(ActualWidth - w, 4, w, Math.Max(0, ActualHeight - 15)), 4, 4);
    }
}
