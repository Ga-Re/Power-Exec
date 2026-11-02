namespace PowerExec.Core.Search;

/// <summary>Eigene Reihenfolge je Gruppe (Drag &amp; Drop im Panel).</summary>
public static class ItemOrdering
{
    /// <summary>Sortiert: Einträge mit eigener Position zuerst (in dieser Reihenfolge), danach der Rest in der vorgegebenen Reihenfolge.</summary>
    public static List<T> Apply<T>(IEnumerable<T> items, Func<T, string> pathOf, IReadOnlyList<string>? order)
    {
        var list = items.ToList();
        if (order == null || order.Count == 0) return list;
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < order.Count; i++) index.TryAdd(order[i], i);
        return list
            .Select((item, i) => (item, i, pos: index.TryGetValue(pathOf(item), out var p) ? p : int.MaxValue))
            .OrderBy(x => x.pos).ThenBy(x => x.i)
            .Select(x => x.item)
            .ToList();
    }

    /// <summary>Verschiebt <paramref name="dragged"/> vor bzw. hinter <paramref name="target"/> und liefert die neue Reihenfolge.</summary>
    public static List<string> Move(IReadOnlyList<string> currentOrder, string dragged, string target, bool after)
    {
        var list = currentOrder.Where(p => !p.Equals(dragged, StringComparison.OrdinalIgnoreCase)).ToList();
        var idx = list.FindIndex(p => p.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) list.Add(dragged);
        else list.Insert(after ? idx + 1 : idx, dragged);
        return list;
    }
}
