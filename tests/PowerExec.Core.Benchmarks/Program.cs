using BenchmarkDotNet.Running;
using PowerExec.Core.Benchmarks;
using PowerExec.Core.Config;
using PowerExec.Core.Diagnostics;

// Aufruf: dotnet run -c Release --project tests/PowerExec.Core.Benchmarks -- --filter *
// Kurzlauf: … -- --filter * --job short
// Testbestand prüfen (Anzahl Einträge, Treffer, Gruppen): … -- --check
if (args.Length > 0 && args[0] == "--check")
{
    var data = PerfDataset.Create(Path.Combine(Path.GetTempPath(), "PowerExecPerf"));
    var settings = new AppSettings();
    data.ApplyTo(settings);
    var mirror = new RebuildMirror(settings, data.CreateItems());
    Console.WriteLine($"Gruppen: {GroupManager.All(settings).Count} · Einträge in Settings.Entries: {settings.Entries.Count} · Favoriten: {settings.Favorites.Count} · RunStats: {settings.RunStats.Count}");
    foreach (var q in new[] { "", "d", "disk", "#ad", "typ:ps1 user" })
        Console.WriteLine($"„{q}“ → {mirror.Run(q)}");
    return;
}
BenchmarkSwitcher.FromAssembly(typeof(RebuildMirror).Assembly).Run(args);
