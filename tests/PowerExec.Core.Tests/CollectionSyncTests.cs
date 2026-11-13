using System.Globalization;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using PowerExec.Core.Collections;

namespace PowerExec.Core.Tests;

public class CollectionSyncTests
{
    private sealed class Box(string name) { public string Name { get; } = name; public override string ToString() => Name; }

    private static (ObservableCollection<Box> Target, List<NotifyCollectionChangedAction> Log) Track(params Box[] items)
    {
        var target = new ObservableCollection<Box>(items);
        var log = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => log.Add(e.Action);
        return (target, log);
    }

    [Fact]
    public void KeepsInstancesAndOnlyRemovesWhatIsGone()
    {
        Box a = new("a"), b = new("b"), c = new("c"), d = new("d");
        var (target, log) = Track(a, b, c, d);
        Assert.True(CollectionSync.SyncTo(target, new[] { a, c }));
        Assert.Equal(new[] { a, c }, target);
        Assert.All(log, x => Assert.Equal(NotifyCollectionChangedAction.Remove, x));
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void InsertsNewAndMovesExisting()
    {
        Box a = new("a"), b = new("b"), c = new("c"), n = new("n");
        var (target, log) = Track(a, b, c);
        CollectionSync.SyncTo(target, new[] { c, n, a });
        Assert.Equal(new[] { c, n, a }, target);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, log);
        Assert.Single(log, x => x == NotifyCollectionChangedAction.Add);
    }

    [Fact]
    public void NoChangeRaisesNothing()
    {
        Box a = new("a"), b = new("b");
        var (target, log) = Track(a, b);
        Assert.False(CollectionSync.SyncTo(target, new[] { a, b }));
        Assert.Empty(log);
    }

    [Fact]
    public void FillsEmptyAndEmptiesFull()
    {
        Box a = new("a"), b = new("b");
        var (target, _) = Track();
        CollectionSync.SyncTo(target, new[] { a, b });
        Assert.Equal(new[] { a, b }, target);
        CollectionSync.SyncTo(target, Array.Empty<Box>());
        Assert.Empty(target);
    }

    [Fact]
    public void EqualButDifferentInstancesAreReplaced()
    {
        // Abgleich über Identität: ein neues Objekt mit gleichem Inhalt ersetzt das alte
        Box a = new("a"), a2 = new("a");
        var (target, _) = Track(a);
        CollectionSync.SyncTo(target, new[] { a2 });
        Assert.Same(a2, Assert.Single(target));
    }

    [Fact]
    public void RandomSequencesAlwaysMatch()
    {
        var pool = Enumerable.Range(0, 40).Select(i => new Box(i.ToString(CultureInfo.InvariantCulture))).ToArray();
        var rnd = new Random(3);
        var target = new ObservableCollection<Box>();
        for (int round = 0; round < 300; round++)
        {
            var desired = pool.OrderBy(_ => rnd.Next()).Take(rnd.Next(pool.Length + 1)).ToList();
            CollectionSync.SyncTo(target, desired);
            Assert.Equal(desired, target);
        }
    }
}
