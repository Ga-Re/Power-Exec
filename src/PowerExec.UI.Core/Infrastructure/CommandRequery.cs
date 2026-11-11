namespace PowerExec.UI.Infrastructure;

/// <summary>
/// Quelle für „CanExecute neu prüfen“ aller <see cref="RelayCommand"/>s. Die Oberfläche setzt beim Start
/// ihr <see cref="Backend"/> (WPF: an <c>CommandManager.RequerySuggested</c> gebunden); ohne Oberfläche
/// – z. B. in Tests – gilt <see cref="ManualCommandRequery"/>.
/// </summary>
public static class CommandRequery
{
    public static ICommandRequeryBackend Backend { get; set; } = new ManualCommandRequery();

    /// <summary>Alle Commands bitten, CanExecute neu zu prüfen (entspricht CommandManager.InvalidateRequerySuggested).</summary>
    public static void Invalidate() => Backend.Invalidate();
}

public interface ICommandRequeryBackend
{
    void Add(EventHandler handler);
    void Remove(EventHandler handler);
    void Invalidate();
}

/// <summary>Einfaches Backend ohne Oberfläche: <see cref="Invalidate"/> ruft die Handler sofort auf.</summary>
public sealed class ManualCommandRequery : ICommandRequeryBackend
{
    private readonly List<EventHandler> _handlers = new();
    private readonly Lock _lock = new();

    public void Add(EventHandler handler) { lock (_lock) _handlers.Add(handler); }

    public void Remove(EventHandler handler) { lock (_lock) _handlers.Remove(handler); }

    public void Invalidate()
    {
        EventHandler[] copy;
        lock (_lock) copy = _handlers.ToArray();
        foreach (var h in copy) h(null, EventArgs.Empty);
    }
}
