using System.Collections.Concurrent;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;

namespace PowerExec.Services;

public sealed record FolderStatus(string Path, int Count, IReadOnlyList<ExcludedFile> ExcludedFiles, string? Error)
{
    public int Excluded => ExcludedFiles.Count;

    /// <summary>Dauer des Einlesens.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Angezeigt wird der zwischengespeicherte Stand (Ordner nicht erreichbar oder Start).</summary>
    public bool FromCache { get; init; }
    public DateTime? CachedAtUtc { get; init; }
}

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

    private readonly ScanCache _scanCache = new();
    private Dictionary<string, CachedFolder> _cached = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zeigt beim Start sofort den zwischengespeicherten Stand an, bis der erste Scan fertig ist.</summary>
    public void LoadCache()
    {
        _cached = _scanCache.Load();
        var items = new List<LaunchItem>();
        var statuses = new List<FolderStatus>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _settings.Folders)
        {
            if (!_cached.TryGetValue(folder.Path, out var c)) continue;
            foreach (var it in c.Items)
                if (seen.Add(it.FullPath))
                {
                    items.Add(it);
                    _cache[it.FullPath] = it;
                }
            statuses.Add(new FolderStatus(folder.Path, c.Items.Count, c.Excluded, null) { FromCache = true, CachedAtUtc = c.ScannedUtc });
        }
        if (items.Count == 0) return;
        Items = items;
        FolderStatuses = statuses;
        AppLog.Info($"Zwischenspeicher geladen: {items.Count} Einträge");
        Changed?.Invoke(this, EventArgs.Empty);
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
        var snapshot = _cached.Values.Where(c => settings.Folders.Any(f => f.Path.Equals(c.Path, StringComparison.OrdinalIgnoreCase))).ToList();
        _ = Task.Run(() => _scanCache.Save(snapshot));
    }

    private (List<LaunchItem>, List<FolderStatus>) ScanAll(AppSettings settings)
    {
        var items = new List<LaunchItem>();
        var statuses = new List<FolderStatus>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timeout = TimeSpan.FromSeconds(Math.Max(2, settings.ScanTimeoutSeconds));

        foreach (var folder in settings.Folders)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // Das reine Durchsuchen bekommt ein Zeitlimit – eine nicht erreichbare Freigabe kann sonst minutenlang hängen.
            var scanTask = Task.Run(() => FolderScanner.Scan(folder, settings));
            ScanResult? result = scanTask.Wait(timeout) ? scanTask.Result : null;
            string? error = result == null ? $"Keine Antwort nach {timeout.TotalSeconds:0} s" : result.Error;

            if (result == null || result.Error != null)
            {
                sw.Stop();
                if (_cached.TryGetValue(folder.Path, out var c) && c.Items.Count > 0)
                {
                    foreach (var it in c.Items)
                        if (seen.Add(it.FullPath)) items.Add(it);
                    statuses.Add(new FolderStatus(folder.Path, c.Items.Count, c.Excluded,
                        $"{error} – angezeigt wird der Stand vom {c.ScannedUtc.ToLocalTime():dd.MM. HH:mm}")
                    { Duration = sw.Elapsed, FromCache = true, CachedAtUtc = c.ScannedUtc });
                }
                else
                {
                    statuses.Add(new FolderStatus(folder.Path, 0, Array.Empty<ExcludedFile>(), error) { Duration = sw.Elapsed });
                }
                AppLog.Warn($"Ordner {folder.Path}: {error}");
                continue;
            }

            var loaded = new LaunchItem?[result.Files.Count];
            Parallel.For(0, result.Files.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            {
                loaded[i] = GetOrLoad(result.Files[i], folder.Path);
            });
            var folderItems = loaded.Where(i => i != null).Cast<LaunchItem>().ToList();
            foreach (var it in folderItems)
                if (seen.Add(it.FullPath)) items.Add(it);
            sw.Stop();
            statuses.Add(new FolderStatus(folder.Path, result.Files.Count, result.Excluded, null) { Duration = sw.Elapsed });
            _cached[folder.Path] = new CachedFolder
            {
                Path = folder.Path, ScannedUtc = DateTime.UtcNow, Items = folderItems, Excluded = result.Excluded.ToList()
            };
            AppLog.Info($"Ordner {folder.Path}: {result.Files.Count} Dateien, {result.ExcludedCount} per Muster ausgeschlossen, {sw.ElapsedMilliseconds} ms");
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
            item.ZoneId = WindowsMetadata.ReadZoneId(path);
            _cache[path] = item;
            return item;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Lädt die Metadaten eines Eintrags neu (z. B. vor dem Öffnen des Dialogs).</summary>
    private readonly ConcurrentDictionary<string, (DateTime Checked, LaunchItem Item)> _singles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _singleLoading = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Einzelne Datei außerhalb der durchsuchten Ordner (eigener Eintrag „Datei“): liefert die zwischengespeicherten
    /// Metadaten (Synopsis, Parameter …) oder vorerst den einfachen Eintrag und lädt im Hintergrund nach.
    /// </summary>
    public LaunchItem DescribeSingle(LaunchItem basic)
    {
        if (_singles.TryGetValue(basic.FullPath, out var c) && DateTime.UtcNow - c.Checked < TimeSpan.FromMinutes(5))
            return Merge(c.Item, basic);
        if (_singleLoading.TryAdd(basic.FullPath, 0))
        {
            var path = basic.FullPath;
            _ = Task.Run(() =>
            {
                try
                {
                    var loaded = _loader.Load(path, basic.RootFolder);
                    loaded.ZoneId = WindowsMetadata.ReadZoneId(path);
                    if (!File.Exists(path)) loaded.ParseError = "Datei nicht gefunden";
                    _singles[path] = (DateTime.UtcNow, loaded);
                }
                catch { }
                finally
                {
                    _singleLoading.TryRemove(path, out _);
                }
                _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
            });
        }
        return c.Item != null ? Merge(c.Item, basic) : basic;
    }

    private static LaunchItem Merge(LaunchItem loaded, LaunchItem basic)
    {
        loaded.CustomKind = basic.CustomKind;
        if (basic.DisplayName.Length > 0 && !basic.DisplayName.Equals(Path.GetFileName(basic.FullPath), StringComparison.OrdinalIgnoreCase))
            loaded.DisplayName = basic.DisplayName;
        if (basic.Synopsis.Length > 0) loaded.Synopsis = basic.Synopsis;
        return loaded;
    }

    /// <summary>Metadaten einer einzelnen Datei lesen (ohne Zwischenspeicher).</summary>
    public LaunchItem LoadFile(string path, string rootFolder) => _loader.Load(path, rootFolder);

    public LaunchItem Reload(LaunchItem item)
    {
        var fresh = _loader.Load(item.FullPath, item.RootFolder);
        fresh.ZoneId = WindowsMetadata.ReadZoneId(item.FullPath);
        if (item.CustomKind != null)
        {
            _singles[item.FullPath] = (DateTime.UtcNow, fresh);
            return Merge(fresh, item);
        }
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
