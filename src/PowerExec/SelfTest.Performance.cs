using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Diagnostics;
using PowerExec.Core.Search;
using PowerExec.Services;

namespace PowerExec;

/// <summary>
/// Leistungsmessung im Selbsttest: Testbestand mit 500 Einträgen (siehe <see cref="PerfDataset"/>) einlesen,
/// Panel öffnen, „disk“ Zeichen für Zeichen tippen und löschen, Filter wechseln. Gemessen wird je Schritt
/// die Zeit bis zum Ende des Rebuilds (synchron) und bis der UI-Thread wieder frei ist (inkl. Layout).
/// Ergebnis: selftest-perf.md und selftest-perf.json neben der Fortschrittsdatei. Noch ohne Grenzwert.
/// </summary>
public sealed partial class AppController
{
    private const int PerfRounds = 5;

    private sealed record PerfSample(string Scenario, string Input, double SyncMs, double TotalMs, string Changes = "");

    private const string TypingReuse = "Tippen";
    private const string TypingFull = "Tippen ohne Wiederverwendung";

    private async Task RunPerformanceAsync(string baseDir)
    {
        var dispatcher = Application.Current.Dispatcher;
        var samples = new List<PerfSample>();
        var host = _hosts[0];
        var vm = host.ViewModel;
        var monitor = ScreenService.TriggerMonitors(host.Config)[0];
        var animation = Settings.Animation;
        var health = Health;

        async Task<(double Sync, double Total)> Measure(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            var sync = sw.Elapsed.TotalMilliseconds;
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            return (sync, sw.Elapsed.TotalMilliseconds);
        }

        try
        {
            Step("Leistung: Testbestand anlegen");
            var data = PerfDataset.Create(Path.Combine(baseDir, "perf"));
            data.WriteFiles();
            data.ApplyTo(Settings);
            // Erreichbarkeitsprüfungen eigener Einträge laufen sonst nebenher und verfälschen die Messung
            Health = null!;
            Settings.Animation = AnimationMode.Off;

            Step("Leistung: Einlesen");
            var scanWatch = Stopwatch.StartNew();
            Repository.UpdateSettings(Settings);
            await Repository.ScanAsync();
            scanWatch.Stop();
            await Settle();
            var visibleCount = vm.CollectVisible().Count;
            if (visibleCount < 450) Fail($"Leistung: Testbestand unvollständig ({visibleCount} statt ≈ 500 Einträge)");

            Step("Leistung: Panel öffnen");
            for (int round = 0; round <= PerfRounds; round++)
            {
                host.Window.HidePanel();
                await Settle();
                var t = await Measure(() => host.Window.ShowPanel(monitor, viaHotkey: true));
                if (round > 0) samples.Add(new PerfSample("Panel öffnen", "", t.Sync, t.Total)); // Runde 0 = Aufwärmen
            }

            // A/B im selben Prozess (Runner schwanken stark): abwechselnd mit und ohne Wiederverwendung der ViewModels
            Step("Leistung: Tippen (A/B)");
            for (int round = 0; round <= PerfRounds; round++)
                foreach (var reuse in new[] { true, false })
                {
                    vm.ReuseForSearch = reuse;
                    for (int i = 0; i < PerfDataset.TypingSequence.Count; i++)
                    {
                        var text = PerfDataset.TypingSequence[i];
                        // Beim Löschen (zweite Hälfte) eigene Zeile: andere Richtung, andere Listenänderungen
                        var label = i <= 3 ? text : (text.Length == 0 ? "leer" : text) + " ⌫";
                        var t = await Measure(() => vm.SearchText = text);
                        if (round > 0) samples.Add(new PerfSample(reuse ? TypingReuse : TypingFull, label, t.Sync, t.Total, vm.LastSyncStats.ToString()));
                    }
                }
            vm.ReuseForSearch = true;
            vm.SearchText = "disk";
            if (!vm.AllItems.Any()) Fail("Leistung: „disk“ findet im Testbestand nichts");
            vm.SearchText = string.Empty;

            Step("Leistung: Filter wechseln");
            for (int round = 0; round <= PerfRounds; round++)
                foreach (var filter in new[] { TypeFilter.Scripts, TypeFilter.All })
                {
                    var t = await Measure(() => vm.Filter = filter);
                    if (round > 0) samples.Add(new PerfSample("Filter", filter.ToString(), t.Sync, t.Total, vm.LastSyncStats.ToString()));
                }

            host.Window.HidePanel();
            await Settle();
            WritePerformanceReport(samples, visibleCount, scanWatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            Fail("Leistungsmessung: " + ex);
        }
        finally
        {
            Health = health;
            Settings.Animation = animation;
        }
    }

    private void WritePerformanceReport(List<PerfSample> samples, int itemCount, double scanMs)
    {
        static double Percentile(IEnumerable<double> values, double p)
        {
            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0) return 0;
            var index = (int)Math.Ceiling(p * sorted.Count) - 1;
            return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
        }
        string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        var rows = samples.GroupBy(s => (s.Scenario, s.Input)).Select(g => new
        {
            g.Key.Scenario,
            Input = g.Key.Input,
            Count = g.Count(),
            SyncMedian = Percentile(g.Select(s => s.SyncMs), 0.5),
            TotalMedian = Percentile(g.Select(s => s.TotalMs), 0.5),
            TotalMax = g.Max(s => s.TotalMs),
            Changes = g.Last().Changes
        }).ToList();
        var typing = samples.Where(s => s.Scenario == TypingReuse).ToList();
        var typingFull = samples.Where(s => s.Scenario == TypingFull).ToList();
        var summary = new
        {
            Version = UpdateService.CurrentVersionText,
            Machine = $"{Environment.OSVersion.VersionString} · {Environment.ProcessorCount} Kerne",
            Items = itemCount,
            ScanMs = scanMs,
            TypingMeanMs = typing.Count == 0 ? 0 : typing.Average(s => s.TotalMs),
            TypingP95Ms = Percentile(typing.Select(s => s.TotalMs), 0.95),
            TypingMaxMs = typing.Count == 0 ? 0 : typing.Max(s => s.TotalMs),
            TypingFullMeanMs = typingFull.Count == 0 ? 0 : typingFull.Average(s => s.TotalMs),
            Rows = rows
        };

        var md = new StringBuilder();
        md.AppendLine($"### Leistungsmessung Power-Exec {summary.Version}");
        md.AppendLine();
        md.AppendLine($"{itemCount} Einträge · {summary.Machine} · Einlesen {F(scanMs)} ms · {PerfRounds} Runden nach einer Aufwärmrunde");
        md.AppendLine();
        md.AppendLine($"**Tippen je Tastendruck (bis UI-Thread frei):** Mittel {F(summary.TypingMeanMs)} ms · p95 {F(summary.TypingP95Ms)} ms · Max {F(summary.TypingMaxMs)} ms · Ziel 0.17: < 50 ms");
        md.AppendLine();
        md.AppendLine($"**A/B im selben Lauf:** ohne Wiederverwendung Mittel {F(summary.TypingFullMeanMs)} ms · mit {F(summary.TypingMeanMs)} ms" +
                      (summary.TypingMeanMs > 0 ? $" · Faktor {summary.TypingFullMeanMs / summary.TypingMeanMs:0.00}" : string.Empty));
        md.AppendLine();
        md.AppendLine("| Szenario | Eingabe | Rebuild (Median) | bis frei (Median) | bis frei (Max) | Listenänderungen |");
        md.AppendLine("|---|---|---:|---:|---:|---|");
        foreach (var r in rows)
            md.AppendLine($"| {r.Scenario} | {(r.Input.Length == 0 ? "–" : "`" + r.Input + "`")} | {F(r.SyncMedian)} ms | {F(r.TotalMedian)} ms | {F(r.TotalMax)} ms | {r.Changes} |");

        var dir = _progressFile != null ? Path.GetDirectoryName(Path.GetFullPath(_progressFile))! : Path.GetTempPath();
        File.WriteAllText(Path.Combine(dir, "selftest-perf.md"), md.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "selftest-perf.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Step($"Leistung: Tippen Mittel {F(summary.TypingMeanMs)} ms, p95 {F(summary.TypingP95Ms)} ms, Max {F(summary.TypingMaxMs)} ms");
    }
}
