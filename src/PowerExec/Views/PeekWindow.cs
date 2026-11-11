using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using PowerExec.Core.Config;
using PowerExec.Desktop.Interop;

namespace PowerExec.Desktop.Views;

/// <summary>
/// Vorschau (Leertaste) als eigenes Fenster neben dem Panel. Es nimmt keinen Fokus: ↑/↓ und Leertaste wirken weiter im
/// Panel, Klicks auf die Schaltflächen funktionieren trotzdem. Der Inhalt ist die Vorschau-Karte aus dem Panel (gleiche
/// Ressourcen, gleiches Aussehen).
/// </summary>
internal sealed class PeekWindow : Window
{
    private const double CardWidth = 460;
    private const double Gap = 6;

    public PeekWindow(FrameworkElement card, ResourceDictionary resources, FrameworkElement fontSource)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Title = L.T("Preview");
        Resources = resources;
        SetBinding(FontFamilyProperty, new Binding(nameof(FontFamily)) { Source = fontSource });
        SetBinding(FontSizeProperty, new Binding(nameof(FontSize)) { Source = fontSource });
        SetBinding(TextElement.ForegroundProperty, new Binding(nameof(Foreground)) { Source = fontSource });
        Content = card;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.MakeToolWindow(hwnd, noActivate: true);
            PanelWindowNative.DisableTransitions(hwnd);
        };
    }

    /// <summary>Bildschirmbereich in Pixeln (für das automatische Einfahren des Panels).</summary>
    public System.Drawing.Rectangle PixelRect { get; private set; }

    /// <summary>
    /// Neben das Panel legen, zur Bildschirmmitte hin (Panel rechts → Vorschau links davon usw.), innerhalb des Arbeitsbereichs.
    /// </summary>
    /// <param name="panel">Panel in Pixeln.</param>
    /// <param name="workArea">Arbeitsbereich des Monitors in Pixeln.</param>
    /// <param name="scale">Skalierung des Monitors (1 = 96 dpi).</param>
    public void PlaceBeside(System.Drawing.Rectangle panel, System.Drawing.Rectangle workArea, double scale, ScreenEdge edge)
    {
        var w = (int)Math.Round(CardWidth * scale);
        var gap = (int)Math.Round(Gap * scale);
        var h = Math.Min(panel.Height, (int)Math.Round(720 * scale));
        int x, y;
        switch (edge)
        {
            case ScreenEdge.Left:
                x = panel.Right + gap; y = panel.Top;
                break;
            case ScreenEdge.Top:
                h = Math.Min(workArea.Bottom - panel.Bottom - gap, (int)Math.Round(560 * scale));
                x = panel.Left + (panel.Width - w) / 2; y = panel.Bottom + gap;
                break;
            case ScreenEdge.Bottom:
                h = Math.Min(panel.Top - workArea.Top - gap, (int)Math.Round(560 * scale));
                x = panel.Left + (panel.Width - w) / 2; y = panel.Top - gap - h;
                break;
            default: // rechts
                x = panel.Left - gap - w; y = panel.Top;
                break;
        }
        h = Math.Max(h, (int)Math.Round(200 * scale));
        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - w));
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - h));
        PixelRect = new System.Drawing.Rectangle(x, y, w, h);
        Left = x / scale;
        Top = y / scale;
        Width = w / scale;
        Height = h / scale;
    }
}
