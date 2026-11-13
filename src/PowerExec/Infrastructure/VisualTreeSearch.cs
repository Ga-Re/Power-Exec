using System.Windows;
using System.Windows.Media;

namespace PowerExec.Desktop.Infrastructure;

/// <summary>Suche nach übergeordneten Elementen – über den sichtbaren Baum, bei Inhalten ohne Darstellung über den logischen.</summary>
internal static class VisualTreeSearch
{
    /// <summary>Das Element selbst oder das nächste übergeordnete vom Typ <typeparamref name="T"/>; null, wenn es keins gibt.</summary>
    public static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    /// <summary>Liegt das Element in einem Element vom Typ <typeparamref name="T"/> (oder ist selbst eins)?</summary>
    public static bool IsInside<T>(DependencyObject? d) where T : DependencyObject => FindAncestor<T>(d) != null;
}
