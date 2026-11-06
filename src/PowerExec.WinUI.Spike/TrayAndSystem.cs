using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using PowerExec.Infrastructure;

namespace PowerExec.WinUI.Spike;

/// <summary>Tray-Symbol mit Menü (P3) über H.NotifyIcon; Linksklick zeigt/versteckt das Panel.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIcon(App app, PanelWindow panel, HoverZoneWindow zone)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Panel zeigen", Command = new RelayCommand(panel.SlideIn) });
        var backdrops = new MenuFlyoutSubItem { Text = "Hintergrund" };
        backdrops.Items.Add(new MenuFlyoutItem { Text = "Klarglas", Command = new RelayCommand(() => panel.SetBackdrop(SpikeBackdrop.Clear)) });
        backdrops.Items.Add(new MenuFlyoutItem { Text = "Acrylic", Command = new RelayCommand(() => panel.SetBackdrop(SpikeBackdrop.Acrylic)) });
        backdrops.Items.Add(new MenuFlyoutItem { Text = "Mica", Command = new RelayCommand(() => panel.SetBackdrop(SpikeBackdrop.Mica)) });
        menu.Items.Add(backdrops);
        var busy = new ToggleMenuFlyoutItem { Text = "Aktivität simulieren (Zone pulsiert)" };
        busy.Click += (_, _) => zone.SetBusy(busy.IsChecked);
        menu.Items.Add(busy);
        var autostart = new ToggleMenuFlyoutItem { Text = "Mit Windows starten", IsChecked = Autostart.IsEnabled };
        autostart.Click += (_, _) => Autostart.Set(autostart.IsChecked);
        menu.Items.Add(autostart);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Protokoll öffnen", Command = new RelayCommand(() =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SpikeLog.FilePath) { UseShellExecute = true })?.Dispose()) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Beenden", Command = new RelayCommand(() => app.Quit()) });

        _icon = new TaskbarIcon
        {
            ToolTipText = "Power-Exec (WinUI-Prototyp)",
            IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "PowerExec.ico"))),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(panel.Toggle)
        };
        _icon.ForceCreate();
        IsCreated = true;
    }

    public bool IsCreated { get; private set; }

    public void Dispose() => _icon.Dispose();
}

/// <summary>
/// Globales Tastenkürzel (P3): Strg+Alt+Leertaste wie in der echten App; ist es belegt (z. B. weil die WPF-Version
/// läuft), Strg+Alt+Umschalt+Leertaste.
/// </summary>
public sealed class Hotkey : IDisposable
{
    private const int Id = 0x5E01;
    private readonly IntPtr _hwnd;
    private readonly WinUIEx.Messaging.WindowMessageMonitor _monitor;

    public Hotkey(PanelWindow window, Action pressed)
    {
        _hwnd = window.Handle;
        _monitor = new WinUIEx.Messaging.WindowMessageMonitor(window);
        _monitor.WindowMessageReceived += (_, e) =>
        {
            if (e.Message.MessageId == Native.WM_HOTKEY && (int)e.Message.WParam == Id)
            {
                pressed();
                e.Handled = true;
            }
        };
        if (Native.RegisterHotKey(_hwnd, Id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_SPACE))
            Display = "Strg+Alt+Leertaste";
        else if (Native.RegisterHotKey(_hwnd, Id, Native.MOD_CONTROL | Native.MOD_ALT | 0x0004 /* MOD_SHIFT */ | Native.MOD_NOREPEAT, Native.VK_SPACE))
            Display = "Strg+Alt+Umschalt+Leertaste";
        SpikeLog.Write("Tastenkürzel: " + (Display ?? "nicht registriert"));
    }

    /// <summary>Registriertes Kürzel; null = keins frei.</summary>
    public string? Display { get; }

    public void Dispose()
    {
        Native.UnregisterHotKey(_hwnd, Id);
        _monitor.Dispose();
    }
}

/// <summary>Autostart (P3) über HKCU\…\Run mit <c>--background</c>: startet ohne Panel, nur Zone und Tray.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "PowerExecWinUISpike";

    public static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(Name, Command);
        else key.DeleteValue(Name, throwOnMissingValue: false);
        SpikeLog.Write($"Autostart: {(enabled ? Command : "aus")}");
    }
}
