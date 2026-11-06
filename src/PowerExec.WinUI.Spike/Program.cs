using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace PowerExec.WinUI.Spike;

/// <summary>
/// Einstieg. Argumente:
/// <c>--background</c> (Autostart: nur Hover-Zone und Tray, kein Panel),
/// <c>--smoke &lt;datei&gt;</c> (spielt die Prüfpunkte automatisch durch, schreibt Messwerte als Markdown und beendet sich).
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var options = SpikeOptions.Parse(args);
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(options);
        });
        return App.ExitCode;
    }
}

public sealed record SpikeOptions(bool Background, string? SmokeFile)
{
    public static SpikeOptions Parse(string[] args)
    {
        string? smoke = null;
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--smoke") smoke = i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "PowerExec-winui-smoke.md");
        return new SpikeOptions(args.Contains("--background"), smoke);
    }
}
