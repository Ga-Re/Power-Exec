using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Desktop.Infrastructure;
using PowerExec.Desktop.Services;

namespace PowerExec.Desktop.Views;

/// <summary>
/// Farben eines Panels: Panelfarbe und Deckkraft, das passende Farbschema für Schrift und Symbole, Akzente, Milchglas
/// (Helligkeitsschicht und Tönung) und die Werte des Glas-Weichzeichners. Die Werte landen als Ressourcen am Fenster
/// und überdecken dort die des Designs.
/// </summary>
internal sealed class PanelAppearance
{
    private readonly PanelWindow _window;
    private readonly AppController _app;
    private readonly Func<PanelConfig> _config;
    private readonly Func<bool> _darkSurface;
    private readonly PanelBackdrop _backdrop;
    private readonly PanelGlassHost _glass;

    /// <param name="window">Das Panelfenster (Ressourcen, Panelrahmen, Milchglas-Schichten).</param>
    /// <param name="app">App-Kern (Einstellung „Akzentfarbe von Windows“).</param>
    /// <param name="config">Aktuelle Einstellungen des Panels.</param>
    /// <param name="darkSurface">Ist die Panelfläche dunkel?</param>
    /// <param name="backdrop">Tatsächlich laufende Variante des Hintergrunds.</param>
    /// <param name="glass">Weichzeichner und Kindfenster.</param>
    public PanelAppearance(PanelWindow window, AppController app, Func<PanelConfig> config, Func<bool> darkSurface, PanelBackdrop backdrop, PanelGlassHost glass)
    {
        _window = window;
        _app = app;
        _config = config;
        _darkSurface = darkSurface;
        _backdrop = backdrop;
        _glass = glass;
    }

    private ResourceDictionary Resources => _window.Resources;

    private static readonly string[] Keys =
        ["Bg.Panel", "Bg.Hover", "Bg.Selected", "Bg.Subtle", "Bg.Input", "Bg.Chip", "Border.Panel", "Border.Default",
         "Accent", "Accent.Hover", "Accent.Pressed", "OnAccent", "Search.Highlight",
         "Bg.Card", "Bg.Tile", "Border.Control", "Text.Primary", "Text.Secondary", "Text.Tertiary"];

    /// <summary>Farben der Variante, die zum laufenden Windows-Design gehört (Design mit hell/dunkel-Paar).</summary>
    private DesignColors DesignColors => Appearance.Effective(_app.Settings, _config(), ThemeService.IsDark);

    private static bool IsOwnPalette(ResourceDictionary d) =>
        d.Source?.OriginalString is { } src && (src.EndsWith("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) || src.EndsWith("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Panelfarbe und Deckkraft anwenden. Bei einer hellen Farbe im dunklen Design (oder umgekehrt)
    /// wird für das Panel das passende Farbschema für Schrift und Symbole verwendet.
    /// </summary>
    public void Apply()
    {
        var s = _config();
        var colors = DesignColors;
        if (_window.PanelBorder.Child is FrameworkElement panelContent) WindowHelper.ApplyContentScale(panelContent);
        WindowHelper.ApplyFont(_window);
        // Nur das eigene Gegen-Farbschema entfernen – Themes/Fluent.xaml und das Fluent-Design bleiben eingebunden
        foreach (var d in Resources.MergedDictionaries.Where(IsOwnPalette).ToList()) Resources.MergedDictionaries.Remove(d);
        foreach (var key in Keys) Resources.Remove(key);
        double opacity = s.EffectiveOpacity / 100.0; // Vollton: deckend
        // Deckkraft wie am Regler eingestellt (auch bei hellen Panels)
        if (ThemeService.IsHighContrast) opacity = 1; // Kontrastdesign: deckend, Systemfarben
        ApplyBackdrop();

        if (!ThemeService.IsHighContrast && PanelColors.TryParse(colors.PanelColor, out var rgb))
        {
            bool light = PanelColors.IsLight(rgb);
            if (light == ThemeService.IsDark)
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(light ? "pack://application:,,,/Themes/Light.xaml" : "pack://application:,,,/Themes/Dark.xaml")
                });
                // Windows-Akzentfarbe auch im Panel mit Gegen-Design behalten (passend aufgehellt bzw. abgedunkelt)
                if (_app.Settings.UseWindowsAccent && ThemeService.WindowsAccent() is { } accent)
                {
                    var a = Appearance.Derive(accent, dark: !light);
                    Resources.MergedDictionaries[^1]["Accent"] = a.Accent.ToBrush();
                    Resources.MergedDictionaries[^1]["Accent.Hover"] = a.Hover.ToBrush();
                    Resources.MergedDictionaries[^1]["OnAccent"] = a.OnAccent.ToBrush();
                }
            }
            var d = PanelColors.Derive(rgb);
            Resources["Bg.Panel"] = rgb.ToBrush(opacity);
            Resources["Bg.Hover"] = d.Hover.ToBrush(HoverAlpha(opacity));
            Resources["Bg.Selected"] = d.Selected.ToBrush(SelectedAlpha(opacity));
            Resources["Bg.Chip"] = d.Selected.ToBrush();
            Resources["Bg.Input"] = d.Input.ToBrush();
            Resources["Border.Panel"] = d.Border.ToBrush();
            Resources["Border.Default"] = d.Border.ToBrush();
        }
        else if (opacity < 1 && Application.Current.Resources["Bg.Panel"] is SolidColorBrush themeBrush)
        {
            Resources["Bg.Panel"] = themeBrush.Color.ToRgb().ToBrush(opacity);
            // Glas: auch Auswahl und Hover lassen den Weichzeichner durchscheinen
            if (_backdrop == PanelBackdrop.Glass)
            {
                if (Application.Current.Resources["Bg.Hover"] is SolidColorBrush h)
                    Resources["Bg.Hover"] = h.Color.ToRgb().ToBrush(HoverAlpha(opacity));
                if (Application.Current.Resources["Bg.Selected"] is SolidColorBrush sel)
                    Resources["Bg.Selected"] = sel.Color.ToRgb().ToBrush(SelectedAlpha(opacity));
            }
        }
        if (!ThemeService.IsHighContrast && colors.Palette is { IsEmpty: false } palette) ApplyPalette(palette, opacity);
        ApplyAccent();
        // Glas: auch Flächen in den Details (Parameter) lassen den Weichzeichner durchscheinen
        if (_backdrop == PanelBackdrop.Glass && !ThemeService.IsHighContrast && _window.TryFindResource("Bg.Subtle") is SolidColorBrush subtle)
            Resources["Bg.Subtle"] = subtle.Color.ToRgb().ToBrush(HoverAlpha(opacity));
        // Fluent-Bedienelemente (WPF UI): Hell/Dunkel nach der Panelfläche, Flächenfarben setzt das Panel selbst
        FluentTheme.Attach(_window, dark: _darkSurface, mapPalette: false);
        ApplyFrost();
    }

    /// <summary>
    /// Ohne „Akzentfarbe von Windows“: Akzente (Auswahl, Knöpfe, Fokus, Suchtreffer) aus der eigenen Akzentfarbe des Panels
    /// bzw. aus der Panelfarbe – lesbar aufgehellt oder abgedunkelt für die Panelfläche.
    /// </summary>
    private void ApplyAccent()
    {
        var s = DesignColors;
        if (ThemeService.IsHighContrast || _app.Settings.UseWindowsAccent) return;
        if (!PanelColors.TryParse(s.AccentColor, out var source) && !PanelColors.TryParse(s.PanelColor, out source)) return;
        bool dark = _darkSurface();
        // Gegen die tatsächliche Panelfläche prüfen: auf mittleren Tönen reichte die Annahme „hell“ bzw. „dunkel“ nicht
        var a = PanelColors.TryParse(s.PanelColor, out var surface) ? Appearance.Derive(source, dark, surface) : Appearance.Derive(source, dark);
        Resources["Accent"] = a.Accent.ToBrush();
        Resources["Accent.Hover"] = a.Hover.ToBrush();
        Resources["Accent.Pressed"] = a.Pressed.ToBrush();
        Resources["OnAccent"] = a.OnAccent.ToBrush();
        Resources["Search.Highlight"] = a.Highlight.ToBrush(dark ? 0.33 : 0.25);
    }

    /// <summary>
    /// Palette des Designs: was es ausdrücklich setzt, überdeckt die aus der Panelfarbe abgeleiteten Farben. Flächen
    /// behalten ihre Durchsicht (Glas), Text und Linien sind deckend.
    /// </summary>
    private void ApplyPalette(DesignPalette p, double opacity)
    {
        if (PanelColors.TryParse(p.Card, out var card))
        {
            Resources["Bg.Card"] = card.ToBrush(HoverAlpha(opacity));
            Resources["Bg.Tile"] = card.ToBrush(HoverAlpha(opacity));
            Resources["Bg.Subtle"] = card.ToBrush(HoverAlpha(opacity));
        }
        if (PanelColors.TryParse(p.Input, out var input)) Resources["Bg.Input"] = input.ToBrush();
        if (PanelColors.TryParse(p.Line, out var line))
        {
            Resources["Border.Default"] = line.ToBrush();
            Resources["Border.Panel"] = line.ToBrush();
            Resources["Border.Control"] = line.ToBrush();
        }
        if (PanelColors.TryParse(p.Selected, out var selected))
        {
            Resources["Bg.Selected"] = selected.ToBrush(SelectedAlpha(opacity));
            Resources["Bg.Chip"] = selected.ToBrush();
            // Hover: halber Weg zwischen Fläche und Auswahl
            var surface = _window.TryFindResource("Bg.Panel") is SolidColorBrush b ? b.Color.ToRgb() : selected;
            Resources["Bg.Hover"] = PanelColors.Blend(surface, selected, 0.5).ToBrush(HoverAlpha(opacity));
        }
        if (PanelColors.TryParse(p.Text, out var text)) Resources["Text.Primary"] = text.ToBrush();
        if (PanelColors.TryParse(p.TextSecondary, out var second))
        {
            Resources["Text.Secondary"] = second.ToBrush();
            Resources["Text.Tertiary"] = second.ToBrush(0.8);
        }
    }

    /// <summary>
    /// Deckkraft von Hover- und Auswahlflächen: bei Klarglas fast deckend (Lesbarkeit über unruhigem Desktop), bei Glas
    /// etwas über der Panel-Deckkraft – der Weichzeichner scheint auch durch ausgeklappte Einträge.
    /// </summary>
    private double HoverAlpha(double opacity) => _backdrop == PanelBackdrop.Glass ? Math.Min(1, opacity + 0.15) : Math.Max(opacity, 0.85);

    private double SelectedAlpha(double opacity) => _backdrop == PanelBackdrop.Glass ? Math.Min(1, opacity + 0.25) : Math.Max(opacity, 0.9);

    /// <summary>
    /// Fast unsichtbare Grundfläche (Deckkraft 1/255) für Glas: Windows lässt Klicks und Ablegen in einem durchsichtigen
    /// Kindfenster dort durchfallen, wo die Deckkraft 0 ist – leere Flächen und Gruppenköpfe ohne Farbe wären taub.
    /// </summary>
    private static readonly SolidColorBrush HitSurface = WpfColors.Frozen(Color.FromArgb(1, 0, 0, 0));

    /// <summary>
    /// Milchglas: von unten Helligkeitsschicht (dunkle Fläche Schwarz, helle Weiß; Stärke vom Milchglas-Regler),
    /// darüber die Panelfarbe als Tönung (mit der eingestellten Deckkraft). Ohne Milchglas
    /// (Regler 0, Kontrastdesign) trägt der Panelrahmen die Panelfarbe wie bisher.
    /// </summary>
    public void ApplyFrost()
    {
        var panel = _window.PanelBorder;
        if (_glass.GlassActive)
        {
            // Glas: Helligkeitsschicht und Tönung liegen im Weichzeichner
            panel.Background = HitSurface;
            _window.FrostBrightness.Background = null;
            _window.FrostTint.Background = null;
            _window.FrostLayer.Visibility = Visibility.Collapsed;
            UpdateGlass();
            return;
        }
        var dark = _darkSurface();
        var alpha = ThemeService.IsHighContrast || !PanelBackdrops.SupportsFrost(_backdrop)
            ? 0 : PanelBackdrops.FrostBrightness(_config().Frost, dark);
        if (alpha <= 0)
        {
            panel.SetResourceReference(Border.BackgroundProperty, "Bg.Panel");
            _window.FrostBrightness.Background = null;
            _window.FrostTint.ClearValue(Border.BackgroundProperty);
            _window.FrostLayer.Visibility = Visibility.Collapsed;
            return;
        }
        panel.Background = Brushes.Transparent;
        _window.FrostBrightness.Background = (dark ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255)).ToBrush(alpha);
        _window.FrostTint.SetResourceReference(Border.BackgroundProperty, "Bg.Panel");
        _window.FrostLayer.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Klarglas zeichnet WPF selbst (transparentes Fenster, Milchglas in <see cref="ApplyFrost"/>). Glas läuft in einem
    /// Fenster ohne Transparenz-Schicht: dahinter hängt der eigene Weichzeichner (<see cref="PanelGlassHost"/>).
    /// </summary>
    public void ApplyBackdrop() => _glass.ApplyBackdrop(_darkSurface(), UpdateGlass);

    /// <summary>Werte des Weichzeichners nachziehen (Blur, Sättigung, Helligkeit, Tönung).</summary>
    private void UpdateGlass() => _glass.Update(_config(), _darkSurface(), GlassTint);

    /// <summary>Tönung des Weichzeichners: Panelfarbe samt Deckkraft.</summary>
    public Color GlassTint => _window.TryFindResource("Bg.Panel") is SolidColorBrush b
        ? Color.FromArgb((byte)Math.Round(b.Color.A * Math.Clamp(b.Opacity, 0, 1)), b.Color.R, b.Color.G, b.Color.B)
        : Colors.Transparent;

    /// <summary>Deckkraft der Helligkeitsschicht (0 = kein Milchglas).</summary>
    public double FrostBrightnessAlpha =>
        _window.FrostLayer.Visibility == Visibility.Visible && _window.FrostBrightness.Background is SolidColorBrush b ? b.Color.A / 255.0 : 0;
}
