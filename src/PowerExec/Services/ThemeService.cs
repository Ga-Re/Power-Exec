using System.Windows;
using Microsoft.Win32;
using PowerExec.Core.Config;

namespace PowerExec.Services;

/// <summary>Tauscht das Farbschema (Dunkel/Hell) zur Laufzeit aus.</summary>
public static class ThemeService
{
    public static bool IsDark { get; private set; } = true;

    public static void Apply(ThemeMode mode)
    {
        IsDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemUsesDarkTheme()
        };
        var dict = new ResourceDictionary
        {
            Source = new Uri(IsDark ? "pack://application:,,,/Themes/Dark.xaml" : "pack://application:,,,/Themes/Light.xaml")
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict;
        else merged.Insert(0, dict);
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v ? v == 0 : true;
        }
        catch
        {
            return true;
        }
    }
}
