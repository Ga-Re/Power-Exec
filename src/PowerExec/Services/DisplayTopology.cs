using System.Windows.Threading;
using Microsoft.Win32;

namespace PowerExec.Services;

/// <summary>
/// Meldet Änderungen an Monitoren, Arbeitsbereich (Taskleiste) und Skalierung – und Momente, in denen Windows Fenster
/// verschoben haben kann (Aufwachen aus dem Standby, Entsperren, Wechsel der Sitzung). Hover-Zonen, Docks und Panels
/// halten Momentaufnahmen der Monitore (<see cref="MonitorInfo"/>); sie werden hierüber aufgefrischt.
/// Mehrere Ereignisse kurz nacheinander (Windows meldet einen Monitorwechsel in Schüben) werden zusammengefasst.
/// </summary>
public sealed class DisplayTopology : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private IReadOnlyList<(MonitorInfo Monitor, double Scale)> _last;
    private bool _force;

    public DisplayTopology(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _last = Snapshot();
        _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Publish(); };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>
    /// Die Anzeige hat sich geändert. <c>true</c>: Monitore, Arbeitsbereich oder Skalierung sind anders als zuvor;
    /// <c>false</c>: gleiche Geometrie, aber Fenster können verschoben worden sein (Standby, Entsperren).
    /// </summary>
    public event Action<bool>? Changed;

    private static IReadOnlyList<(MonitorInfo Monitor, double Scale)> Snapshot() =>
        ScreenService.GetMonitors().Select(m => (Monitor: m, m.Scale)).ToList();

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Schedule(force: true);

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Arbeitsbereich (Taskleiste verschoben, automatisch ausblenden) kommt als „Desktop“ bzw. „General“
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General) Schedule(force: false);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Schedule(force: true);
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Schedule(force: true);
    }

    /// <summary>Die Ereignisse können von einem anderen Thread kommen: auf dem UI-Thread sammeln.</summary>
    private void Schedule(bool force) => _dispatcher.BeginInvoke(() =>
    {
        _force |= force;
        _debounce.Stop();
        _debounce.Start();
    });

    private void Publish()
    {
        var now = Snapshot();
        var geometryChanged = !now.SequenceEqual(_last);
        var force = _force;
        _force = false;
        _last = now;
        if (!geometryChanged && !force) return;
        if (geometryChanged) AppLog.Info($"Display changed: {string.Join("; ", now.Select(m => $"#{m.Monitor.Index} {m.Monitor.Bounds} work {m.Monitor.WorkArea} x{m.Scale:0.##}"))}");
        Changed?.Invoke(geometryChanged);
    }

    public void Dispose()
    {
        _debounce.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}
