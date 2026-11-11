using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Core.Metadata;
using PowerExec.Core.Models;
using PowerExec.Core.Scanning;
using PowerExec.Core.Search;
using System.Drawing;

namespace PowerExec.Core.Tests;

/// <summary>Eigene Reihenfolge der Einträge.</summary>
public class ItemOrderTests
{
    [Fact]
    public void CustomOrderAndMove()
    {
        var items = new[] { "a", "b", "c", "d" };
        Assert.Equal(new[] { "c", "a", "b", "d" }, ItemOrdering.Apply(items, x => x, new[] { "c" }));
        var moved = ItemOrdering.Move(new[] { "a", "b", "c", "d" }, "d", "b", after: false);
        Assert.Equal(new[] { "a", "d", "b", "c" }, moved);
        Assert.Equal(new[] { "a", "c", "d", "b" }, ItemOrdering.Move(new[] { "a", "b", "c", "d" }, "b", "d", after: true));
    }
}
