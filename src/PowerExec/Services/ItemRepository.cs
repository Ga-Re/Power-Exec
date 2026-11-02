using System.Collections.Concurrent;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;

namespace PowerExec.Services;

public sealed record FolderStatus(string Path, int Count, int Excluded, string? Error);

/// <summary>Hält die gefundenen Einträge aktuell: Scannen, Metadaten lesen, Dateiwächter.</summary>
public sealed class ItemRepository : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ItemLoader _loader = new(new WindowsMetadata());
    private readonly ConcurrentDictionary<string, LaunchItem> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _debounce;
    private AppSettings _settings;
    private int _scanVersion;

    public IReadOnlyList<LaunchItem> Items { get; private set; } = Array.Empty<LaunchItem>();
    public IReadOnlyList<FolderStatus> FolderStatuses { get; private set; } = Array.Empty<FolderStatus>();
    public DateTime LastScan { get; private set; }
    public bool IsScanning { get; private set; }

    /// <summary>Wird auf dem UI-Thread ausgelöst, wenn sich die Liste geändert hat.</summary>
    public event EventHandler? Changed;

    public ItemRepository(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(800) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = ScanAsync();
        };
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        SetupWatchers();
        _ = ScanAsync();
    }

    public async Task ScanAsync()
    {
        var version = Interlocked.Increment(ref _scanVersion);
        var settings = _settings;
        IsScanning = true;
        Changed?.Invoke(this, EventArgs.Empty);

        var (items, statuses) = await Task.Run(() => ScanAll(settings));

        if (version != _scanVersion) return; // inzwischen neuer Scan gestartet
        Items = items;
        FolderStatuses = statuses;
        LastScan = DateTime.Now;
        IsScanning = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private (List<LaunchItem>, List<FolderStatus>) ScanAll(AppSettings settings)
    {
        var items = new List<LaunchItem>();
        var statuses = new List<FolderStatus>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in settings.Folders)
        {
            var result = FolderScanner.Scan(folder, settings);
            statuses.Add(new FolderStatus(folder.Path, result.Files.Count, result.ExcludedCount, result.Error));

            var loaded = new LaunchItem?[result.Files.Count];
            Parallel.For(0, result.Files.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            {
                var path = result.Files[i];
                loaded[i] = GetOrLoad(path, folder.Path);
            });
            foreach (var it in loaded)
                if (it != null && seen.Add(it.FullPath)) items.Add(it);
        }

        // Cache auf aktuelle Dateien begrenzen
        foreach (var key in _cache.Keys)
            if (!seen.Contains(key)) _cache.TryRemove(key, out _);
        return (items, statuses);
    }

    private LaunchItem? GetOrLoad(string path, string root)
    {
        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cache.TryGetValue(path, out var cached) && cached.LastWriteTimeUtc == lastWrite &&
                string.Equals(cached.RootFolder, root, StringComparison.OrdinalIgnoreCase))
                return cached;
            var item = _loader.Load(path, root);
            _cache[path] = item;
            return item;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Lädt die Metadaten eines Eintrags neu (z. B. vor dem Öffnen des Dialogs).</summary>
    public LaunchItem Reload(LaunchItem item)
    {
        var fresh = _loader.Load(item.FullPath, item.RootFolder);
        _cache[item.FullPath] = fresh;
        return fresh;
    }

    public void SetupWatchers()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        if (!_settings.AutoRescan) return;

        foreach (var folder in _settings.Folders)
        {
            try
            {
                if (!Directory.Exists(folder.Path)) continue;
                var w = new FileSystemWatcher(folder.Path)
                {
                    IncludeSubdirectories = folder.Recursive,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };
                w.Created += OnFsChange;
                w.Deleted += OnFsChange;
                w.Renamed += OnFsChange;
                w.Changed += OnFsChange;
                w.Error += (_, _) => _dispatcher.BeginInvoke(SetupWatchers);
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch
            {
                // Netzwerkfreigaben unterstützen den Dateiwächter nicht immer – dann nur manuelles Neueinlesen.
            }
        }
    }

    private void OnFsChange(object sender, FileSystemEventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}
