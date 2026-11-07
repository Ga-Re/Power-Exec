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
public sealed partial class SelfTestController
{
    private const int PerfRounds = 5;

    /// <summary>
    /// Grenzwert für die CI: Tippen im Mittel (mit Wiederverwendung). Rund das Doppelte des Stands von 0.17 (~170 ms),
    /// damit Schwankungen der Runner (bis Faktor 1,6) keinen Fehlalarm auslösen, Rückfälle wie ein Neuaufbau aller
    /// Container (600–900 ms) aber sicher auffallen.
    /// </summary>
    private const double PerfTypingLimitMs = 350;

    /// <summary>Rebuild (synchron), danach erzwungenes Layout, danach bis der UI-Thread frei ist (Rendern und Rest).</summary>
    private readonly record struct PerfTiming(double Sync, double Layout, double Total);

    private sealed record PerfSample(string Scenario, string Input, PerfTiming Time, string Changes = "")
    {
        public double SyncMs => Time.Sync;
        public double LayoutMs => Time.Layout;
        public double TotalMs => Time.Total;
    }

    private const string TypingReuse = "Tippen";
    private const string TypingFull = "Tippen ohne Wiederverwendung";

    private async Task RunPerformanceAsync(string baseDir)
    {
        var dispatcher = Application.Current.Dispatcher;
        var samples = new List<PerfSample>();
        var host = WpfHost(0);
        var vm = host.ViewModel;
        var monitor = ScreenService.TriggerMonitors(host.Config)[0];
        var animation = Settings.Animation;
        var health = Health;

        async Task<PerfTiming> Measure(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            var sync = sw.Elapsed.TotalMilliseconds;
            host.Window.UpdateLayout(); // Container und Layout jetzt statt beim nächsten Durchlauf
            var layout = sw.Elapsed.TotalMilliseconds - sync;
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            return new PerfTiming(sync, layout, sw.Elapsed.TotalMilliseconds);
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
                if (round > 0) samples.Add(new PerfSample("Panel öffnen", "", t)); // Runde 0 = Aufwärmen
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
                        if (round > 0) samples.Add(new PerfSample(reuse ? TypingReuse : TypingFull, label, t, vm.LastSyncStats.ToString()));
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
                    if (round > 0) samples.Add(new PerfSample("Filter", filter.ToString(), t, vm.LastSyncStats.ToString()));
                }

            // Diagnose: Welche Elemente erklärt ein Rebuild für ungültig (müssen neu gemessen werden)?
            Step("Leistung: ungültige Elemente");
            var invalid = new List<string>();
            foreach (var (label, action) in new (string, Action)[]
            {
                ("Rebuild ohne Änderung", () => vm.RebuildUnchangedForTest()),
                ("nur RaiseLayout", () => vm.RaiseLayoutForTest()),
                ("Filter Scripte", () => vm.Filter = TypeFilter.Scripts),
                ("Filter Alle", () => vm.Filter = TypeFilter.All),
                ("Tippen „dis“", () => vm.SearchText = "dis"),
                ("Tippen „disk“", () => vm.SearchText = "disk"),
                ("Suche leeren", () => vm.SearchText = string.Empty),
            })
            {
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                action();
                invalid.Add($"| {label} | {CountInvalid(host.Window)} |");
                host.Window.UpdateLayout();
            }

            host.Window.HidePanel();
            await Settle();
            WritePerformanceReport(samples, visibleCount, scanWatch.Elapsed.TotalMilliseconds, invalid);
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

    /// <summary>Elemente mit ungültiger Messung zählen, nach Typ (die häufigsten zuerst).</summary>
    private static string CountInvalid(DependencyObject root)
    {
        int total = 0, invalidCount = 0, untemplated = 0;
        var byType = new Dictionary<string, int>();
        void Walk(DependencyObject d)
        {
            if (d is UIElement e)
            {
                total++;
                if (e is System.Windows.Controls.ContentPresenter && System.Windows.Media.VisualTreeHelper.GetChildrenCount(e) == 0) untemplated++;
                if (!e.IsMeasureValid)
                {
                    invalidCount++;
                    var name = e.GetType().Name;
                    byType[name] = byType.GetValueOrDefault(name) + 1;
                }
            }
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
                Walk(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        var top = string.Join(", ", byType.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value}"));
        return $"{invalidCount} von {total} | {untemplated} | {top}";
    }

    private void WritePerformanceReport(List<PerfSample> samples, int itemCount, double scanMs, List<string> invalid)
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
            LayoutMedian = Percentile(g.Select(s => s.LayoutMs), 0.5),
            TotalMedian = Percentile(g.Select(s => s.TotalMs), 0.5),
            TotalMax = g.Max(s => s.TotalMs),
            Changes = g.Last().Changes
        }).ToList();
        var typing = samples.Where(s => s.Scenario == TypingReuse).ToList();
        var typingFull = samples.Where(s => s.Scenario == TypingFull).ToList();
        var summary = new
        {
            Version = UpdateService.CurrentVersionText,
            Machine = $"{Environment.OSVersion.VersionString} · {Environment.ProcessorCount} Kerne · Render-Stufe {System.Windows.Media.RenderCapability.Tier >> 16}" +
                      ((System.Windows.Media.RenderCapability.Tier >> 16) == 0 ? " (Software)" : " (Grafikkarte)"),
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
        md.AppendLine($"**Tippen je Tastendruck (bis UI-Thread frei):** Mittel {F(summary.TypingMeanMs)} ms · p95 {F(summary.TypingP95Ms)} ms · Max {F(summary.TypingMaxMs)} ms · Grenzwert Mittel ≤ {F(PerfTypingLimitMs)} ms");
        md.AppendLine();
        md.AppendLine($"**A/B im selben Lauf:** ohne Wiederverwendung Mittel {F(summary.TypingFullMeanMs)} ms · mit {F(summary.TypingMeanMs)} ms" +
                      (summary.TypingMeanMs > 0 ? $" · Faktor {summary.TypingFullMeanMs / summary.TypingMeanMs:0.00}" : string.Empty));
        md.AppendLine();
        md.AppendLine("Rebuild = ViewModels und Listen (synchron) · Layout = erzwungenes `UpdateLayout()` (Container, Vorlagen, Messen) · bis frei = alles bis `ApplicationIdle` (inkl. Rendern)");
        md.AppendLine();
        md.AppendLine("| Szenario | Eingabe | Rebuild | Layout | bis frei (Median) | bis frei (Max) | Listenänderungen |");
        md.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var r in rows)
            md.AppendLine($"| {r.Scenario} | {(r.Input.Length == 0 ? "–" : "`" + r.Input + "`")} | {F(r.SyncMedian)} ms | {F(r.LayoutMedian)} ms | {F(r.TotalMedian)} ms | {F(r.TotalMax)} ms | {r.Changes} |");

        md.AppendLine();
        md.AppendLine("**Ungültig nach dem Rebuild** (müssen neu gemessen werden):");
        md.AppendLine();
        md.AppendLine("| Aktion | ungültig | ContentPresenter ohne Vorlage (neu) | häufigste Typen |");
        md.AppendLine("|---|---|---:|---|");
        foreach (var line in invalid) md.AppendLine(line);

        var dir = _progressFile != null ? Path.GetDirectoryName(Path.GetFullPath(_progressFile))! : Path.GetTempPath();
        File.WriteAllText(Path.Combine(dir, "selftest-perf.md"), md.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "selftest-perf.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Step($"Leistung: Tippen Mittel {F(summary.TypingMeanMs)} ms, p95 {F(summary.TypingP95Ms)} ms, Max {F(summary.TypingMaxMs)} ms");
        if (summary.TypingMeanMs > PerfTypingLimitMs)
            Fail($"Leistung: Tippen im Mittel {F(summary.TypingMeanMs)} ms – Grenzwert {F(PerfTypingLimitMs)} ms überschritten (Details: selftest-perf.md)");
    }
}
