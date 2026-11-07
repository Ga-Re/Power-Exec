using PowerExec.Infrastructure;
using PowerExec.Core.Config;
using PowerExec.Services;

namespace PowerExec;

/// <summary>
/// Team-Kataloge (D3): eine Exportdatei auf einer Freigabe abonnieren. Power-Exec prüft beim Start und alle 30 Minuten,
/// ob sie sich geändert hat, und übernimmt die Änderungen (oder meldet sie zum Übernehmen).
/// Katalogeinträge sind schreibgeschützt; eigene Ergänzungen bleiben lokal.
/// </summary>
public sealed partial class AppController
{
    public static readonly TimeSpan CatalogInterval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan CatalogReadTimeout = TimeSpan.FromSeconds(10);
    private IUiTimer? _catalogTimer;

    /// <summary>Kataloge geprüft oder übernommen (Panel-Hinweis und Einstellungen aktualisieren).</summary>
    public event Action? CatalogsChanged;

    public void StartCatalogs()
    {
        _catalogTimer ??= UiDispatcher.CreateTimer(CatalogInterval, () => _ = CheckCatalogsAsync());
        _catalogTimer.Start();
        if (Settings.Catalogs.Count > 0) _ = CheckCatalogsAsync();
    }

    /// <summary>Katalogdatei lesen (mit Zeitlimit, Freigaben können hängen).</summary>
    private static async Task<(string? Content, string? Error)> ReadCatalogAsync(string path)
    {
        try
        {
            var read = Task.Run(() => File.ReadAllText(path));
            if (await Task.WhenAny(read, Task.Delay(CatalogReadTimeout)) != read) return (null, "Zeitüberschreitung beim Lesen (Freigabe nicht erreichbar?)");
            return (await read, null);
        }
        catch (Exception ex)
        {
            return (null, ex is FileNotFoundException or DirectoryNotFoundException ? "Datei nicht gefunden" : ex.Message);
        }
    }

    /// <summary>Alle Kataloge prüfen; <paramref name="force"/> übernimmt auch ohne geänderten Inhalt erneut.</summary>
    public async Task CheckCatalogsAsync(bool force = false)
    {
        bool changed = false;
        var messages = new List<string>();
        foreach (var sub in Settings.Catalogs.Where(c => c.Enabled).ToList())
        {
            var (content, error) = await ReadCatalogAsync(sub.Path);
            sub.LastCheckedUtc = DateTime.UtcNow;
            if (content == null)
            {
                sub.LastError = error ?? "nicht lesbar";
                continue;
            }
            sub.LastError = string.Empty;
            var hash = TeamCatalogs.Hash(content);
            if (!force && hash == sub.LastHash && sub.PendingSummary.Length == 0) continue;
            EntryShare.SharedFile file;
            try { file = EntryShare.Parse(content); }
            catch (Exception ex) { sub.LastError = "Keine gültige Katalogdatei: " + ex.Message; continue; }

            var diff = TeamCatalogs.Diff(Settings, sub, file);
            if (!sub.AutoApply && !force && !diff.IsEmpty)
            {
                sub.PendingSummary = diff.Summary;
                changed = true;
                continue;
            }
            RememberForUndo($"Katalog „{sub.Name}“ aktualisiert");
            TeamCatalogs.Apply(Settings, sub, file);
            sub.LastHash = hash;
            changed = true;
            if (!diff.IsEmpty) messages.Add($"{sub.Name}: {diff.Summary}");
            AppLog.Info($"Katalog {sub.Name} ({sub.Path}): {diff.Summary}");
        }
        SaveSettings();
        if (changed) CommitUserChange();
        CatalogsChanged?.Invoke();
        foreach (var h in _hosts) h.ViewModel.RefreshCatalogs();
        if (messages.Count > 0) ShowUndoToast("Team-Katalog aktualisiert – " + string.Join(" · ", messages));
    }

    /// <summary>Gemeldete Änderung übernehmen (bei „automatisch übernehmen“ aus).</summary>
    public Task ApplyPendingCatalogsAsync() => CheckCatalogsAsync(force: true);

    /// <summary>Katalogdatei abonnieren; Fehlermeldung oder null.</summary>
    public async Task<string?> SubscribeCatalogAsync(string path, string? name = null)
    {
        path = path.Trim().Trim('"');
        if (Settings.Catalogs.Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return "Dieser Katalog ist bereits abonniert.";
        var (content, error) = await ReadCatalogAsync(path);
        if (content == null) return error;
        try { EntryShare.Parse(content); }
        catch (Exception ex) { return "Keine gültige Katalogdatei: " + ex.Message; }
        var baseName = name is { Length: > 0 } ? name : Path.GetFileNameWithoutExtension(path).Replace(".pxentries", string.Empty, StringComparison.OrdinalIgnoreCase);
        var sub = new CatalogSubscription { Path = path, Name = GroupManager.CleanName(baseName.Length > 0 ? baseName : "Team-Katalog") };
        Settings.Catalogs.Add(sub);
        await CheckCatalogsAsync(force: true);
        return null;
    }

    public void UnsubscribeCatalog(CatalogSubscription sub)
    {
        RememberForUndo($"Katalog „{sub.Name}“ abbestellt");
        TeamCatalogs.Unsubscribe(Settings, sub);
        CommitUserChange();
        CatalogsChanged?.Invoke();
        foreach (var h in _hosts) h.ViewModel.RefreshCatalogs();
        ShowUndoToast($"Katalog „{sub.Name}“ abbestellt");
    }

    /// <summary>Katalog, aus dem ein Eintrag stammt (für Hinweis und Schreibschutz).</summary>
    public CatalogSubscription? CatalogOf(CustomEntry? e) =>
        e is { IsFromCatalog: true } ? Settings.Catalogs.FirstOrDefault(c => c.Id == e.CatalogId) : null;
}
