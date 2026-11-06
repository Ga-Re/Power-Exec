using System.Diagnostics;
using System.Text;
using PowerExec.Core.Config;

namespace PowerExec.WinUI.Spike;

/// <summary>
/// <c>--smoke</c>: spielt die Prüfpunkte P1–P4 ohne Benutzer durch und schreibt die Ergebnisse als Markdown
/// (für CI und als Vergleich auf dem eigenen Rechner). Exitcode 0 = alles lief ohne Fehler, 1 = mindestens ein
/// Schritt ist fehlgeschlagen. Sichtprüfungen (Glas, Ruckeln, zweiter Monitor) ersetzt das nicht.
/// </summary>
public static class SmokeTest
{
    private static readonly string[] TypingSequence = { "d", "di", "dis", "disk", "dis", "di", "d", "" };

    public static async Task RunAsync(App app, SpikeHost host, PanelWindow panel, HoverZoneWindow zone, TrayIcon tray, Hotkey hotkey, string file)
    {
        var md = new StringBuilder();
        var failures = 0;
        md.AppendLine("### WinUI-Prototyp – Smoke-Test");
        md.AppendLine();
        md.AppendLine($"{Environment.OSVersion.VersionString} · Build {Environment.OSVersion.Version.Build} · {Environment.ProcessorCount} Kerne · .NET {Environment.Version}");
        md.AppendLine();
        md.AppendLine("| Prüfpunkt | Ergebnis |");
        md.AppendLine("|---|---|");

        async Task Step(string name, Func<Task<string>> action)
        {
            try
            {
                var result = await action();
                md.AppendLine($"| {name} | {result} |");
            }
            catch (Exception ex)
            {
                failures++;
                md.AppendLine($"| {name} | ❌ {ex.GetType().Name}: {ex.Message.Replace('|', '/').Replace('\n', ' ')} |");
                SpikeLog.Write($"Smoke {name}: {ex}");
            }
        }

        await Step("Version", () => Task.FromResult(host.VersionText));

        await Step("Daten (UI.Core)", () => Task.FromResult(
            $"{panel.ViewModel.AllItems.Count()} Einträge in {panel.ViewModel.AllGroups.Count()} Gruppen über PanelViewModel"));

        await Step("P2 Hover-Zone", async () =>
        {
            zone.ShowZone();
            await UiIdle.NextAsync(panel.DispatcherQueue);
            var s = zone.SizePx;
            if (s.Width > 12) throw new InvalidOperationException($"Zone ist {s.Width} px breit (Windows-Mindestbreite?) – sie würde Klicks am Rand abfangen");
            return zone.IsZoneVisible ? $"sichtbar, {s.Width}×{s.Height} px"
                : zone.SuppressedByFullscreen ? "ausgeblendet (Vollbild erkannt)" : throw new InvalidOperationException("Zone nicht sichtbar");
        });

        await Step("P2 Hover-Zone löst aus (Maus an den Rand)", async () =>
        {
            Native.GetCursorPos(out var before);
            var (x, y) = zone.Center;
            Native.SetCursorPos(x + 2, y); // ganz an den Rand: Windows hält den Zeiger am letzten Pixel
            var sw = Stopwatch.StartNew();
            while (!panel.IsShown && sw.ElapsedMilliseconds < 3000) await Task.Delay(20);
            var shown = panel.IsShown;
            var ms = sw.ElapsedMilliseconds;
            Native.SetCursorPos(before.X, before.Y);
            if (shown) await panel.SlideAsync(false);
            return shown ? $"Panel nach {ms} ms ausgefahren (Verweilzeit 250 ms + Gleiten)" : throw new InvalidOperationException("Panel fährt nicht aus");
        });

        await Step("P2 Vollbild-Erkennung", () => Task.FromResult(Native.FullscreenReason() is { } r ? "Vollbild erkannt: " + r : "kein Vollbild (erwartet)"));

        await Step("P2 Pulsieren", async () =>
        {
            zone.SetBusy(true);
            await Task.Delay(300);
            var ok = zone.IsPulsing;
            zone.SetBusy(false);
            return ok ? "läuft" : throw new InvalidOperationException("Pulsieren startet nicht");
        });

        await Step("P3 Tray-Symbol", () => Task.FromResult(tray.IsCreated ? "angelegt (mit Menü)" : throw new InvalidOperationException("Tray-Symbol fehlt")));
        await Step("P3 Tastenkürzel", () => Task.FromResult(hotkey.Display ?? "⚠ kein Kürzel frei"));

        await Step("P3 Autostart", () =>
        {
            var before = Autostart.IsEnabled;
            Autostart.Set(true);
            var on = Autostart.IsEnabled;
            Autostart.Set(before);
            return Task.FromResult(on ? $"Run-Eintrag geschrieben und wieder {(before ? "gesetzt" : "entfernt")}" : throw new InvalidOperationException("Run-Eintrag nicht lesbar"));
        });

        await Step("P1 Ein-/Ausfahren", async () =>
        {
            var slides = new List<SlideStats>();
            for (int i = 0; i < 3; i++)
            {
                if (await panel.SlideAsync(true) is { } a) slides.Add(a);
                await Task.Delay(150);
                if (await panel.SlideAsync(false) is { } b) slides.Add(b);
                await Task.Delay(150);
            }
            if (slides.Count == 0) throw new InvalidOperationException("kein Gleiten gemessen");
            return $"{slides.Count}× · Ø {slides.Average(s => s.Frames):0} Bilder in Ø {slides.Average(s => s.Milliseconds):0} ms · größte Bildlücke {slides.Max(s => s.MaxFrameGapMs):0} ms";
        });

        await panel.SlideAsync(true);

        foreach (var backdrop in new[] { SpikeBackdrop.Clear, SpikeBackdrop.Acrylic, SpikeBackdrop.Mica })
            await Step($"P1 Hintergrund {backdrop}", async () =>
            {
                panel.SetBackdrop(backdrop);
                panel.SetFrost(backdrop == SpikeBackdrop.Clear ? 30 : 0);
                await UiIdle.NextAsync(panel.DispatcherQueue);
                return panel.SystemBackdrop?.GetType().Name ?? throw new InvalidOperationException("kein SystemBackdrop");
            });

        await Step("P4 Tippen (500 Einträge)", async () =>
        {
            panel.TypingSamples.Clear();
            for (int round = 0; round < 6; round++)
            {
                if (round == 1) panel.TypingSamples.Clear(); // erste Runde = Aufwärmen
                foreach (var text in TypingSequence)
                {
                    var before = panel.TypingSamples.Count;
                    panel.SearchBox.Text = text;
                    var sw = Stopwatch.StartNew();
                    while (panel.TypingSamples.Count == before && sw.ElapsedMilliseconds < 5000) await Task.Delay(5);
                }
            }
            var t = panel.TypingSamples;
            var mean = t.Average();
            return $"Ø {mean:0} ms · p95 {PanelWindow.Percentile(t, 95):0} ms · Max {t.Max():0} ms ({t.Count}×)" +
                   (mean > 350 ? " · ⚠ über dem CI-Grenzwert der WPF-App (350 ms)" : string.Empty);
        });

        await Step("P4 Suche filtert", () =>
        {
            panel.ViewModel.SearchText = "typ:exe";
            var exe = panel.ViewModel.AllItems.Count();
            panel.ViewModel.SearchText = string.Empty;
            return Task.FromResult(exe is > 0 and < 500 ? $"typ:exe → {exe} Einträge" : throw new InvalidOperationException($"typ:exe → {exe}"));
        });

        await Step("P4 Gruppen umsortieren", async () =>
        {
            var groups = panel.ViewModel.Groups;
            var first = groups[0];
            groups.Move(0, 2);
            await UiIdle.NextAsync(panel.DispatcherQueue);
            return groups.IndexOf(first) == 2 ? "Verschieben in der Liste ok (Ziehen selbst: Sichtprüfung)" : throw new InvalidOperationException("Reihenfolge stimmt nicht");
        });

        await Step("P4 Ablegen (simuliert)", async () =>
        {
            host.AddDroppedPaths(new[] { @"C:\Tools\procexp64.exe", "https://learn.microsoft.com/windows/apps/" });
            panel.ViewModel.Rebuild();
            await UiIdle.NextAsync(panel.DispatcherQueue);
            var dropped = panel.ViewModel.AllGroups.FirstOrDefault(g => g.FolderPath == SpikeHost.DropFolder)?.Items.Count ?? 0;
            return dropped == 2 ? "2 Einträge in „Abgelegt“ (echtes Ziehen: Sichtprüfung)" : throw new InvalidOperationException($"{dropped} statt 2");
        });

        await panel.SlideAsync(false);

        md.AppendLine();
        md.AppendLine(failures == 0 ? "**Alle Schritte ohne Fehler.**" : $"**{failures} Schritt(e) fehlgeschlagen.**");
        md.AppendLine();
        md.AppendLine("Nicht automatisch prüfbar (Sichtprüfung am eigenen Rechner): Glas-Wirkung, Ruckeln beim Gleiten, Taskleiste und zweiter Monitor, echtes Ziehen aus Explorer/Browser, Lücke beim Gruppen-Ziehen, Tray-Menü, Start ohne Fenster.");
        try { File.WriteAllText(file, md.ToString(), Encoding.UTF8); } catch (Exception ex) { SpikeLog.Write("Smoke-Datei: " + ex.Message); }
        app.Quit(failures == 0 ? 0 : 1);
    }
}
