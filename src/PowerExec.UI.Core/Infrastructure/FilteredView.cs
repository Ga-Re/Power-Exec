using System.Collections.ObjectModel;
using System.Collections.Specialized;
using PowerExec.Core.Collections;

namespace PowerExec.Infrastructure;

/// <summary>
/// Gefilterte und optional sortierte Ansicht einer Quellliste, ohne UI-Framework (ersetzt WPF-<c>CollectionView</c>).
/// <see cref="Items"/> wird bei <see cref="Refresh"/> und bei Änderungen der Quelle per <see cref="CollectionSync"/>
/// abgeglichen – bleibende Elemente behalten ihren Platz in der Oberfläche. Die Sortierung ist stabil.
/// </summary>
public sealed class FilteredView<T> where T : class
{
    private readonly ObservableCollection<T> _source;
    private readonly Func<T, bool> _filter;
    private readonly Func<IEnumerable<T>, IEnumerable<T>>? _sort;

    /// <param name="sort">Reihenfolge der Ansicht, z. B. <c>x => x.OrderBy(r => r.Name)</c>; null = wie die Quelle.</param>
    public FilteredView(ObservableCollection<T> source, Func<T, bool> filter, Func<IEnumerable<T>, IEnumerable<T>>? sort = null)
    {
        _source = source;
        _filter = filter;
        _sort = sort;
        _source.CollectionChanged += OnSourceChanged;
        Refresh();
    }

    /// <summary>Sichtbare Elemente; an die Oberfläche binden.</summary>
    public ObservableCollection<T> Items { get; } = new();

    public int Count => Items.Count;

    /// <summary>Filter und Sortierung neu anwenden (nach Änderung der Filterkriterien oder sortierrelevanter Werte).</summary>
    public void Refresh()
    {
        IEnumerable<T> visible = _source.Where(_filter);
        if (_sort != null) visible = _sort(visible);
        CollectionSync.SyncTo(Items, visible.ToList());
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
}
