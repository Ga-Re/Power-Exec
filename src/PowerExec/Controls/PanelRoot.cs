using System.Windows.Controls;

namespace PowerExec.Desktop.Controls;

/// <summary>
/// Wurzel des Panelinhalts. Bindungen im Panel suchen ihren Datenkontext hier statt am Fenster: bei Glas liegt der
/// Inhalt in einem eigenen Kindfenster über dem Weichzeichner, dort gibt es kein Window darüber.
/// </summary>
public sealed class PanelRoot : Border
{
}
