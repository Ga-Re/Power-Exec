using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.Core.Models;
using PowerExec.Core.Search;

namespace PowerExec.Core.Tests;

/// <summary>Rückgängig-Stapel.</summary>
public class UndoStackTests
{
    [Fact]
    public void UndoStackKeepsNewestWithinCapacity()
    {
        var stack = new UndoStack<int>(capacity: 2);
        Assert.False(stack.TryPop(out _));
        stack.Push(1, "eins");
        stack.Push(2, "zwei");
        stack.Push(3, "drei");
        Assert.Equal(2, stack.Count);
        Assert.Equal("drei", stack.Peek!.Description);
        Assert.True(stack.TryPop(out var step));
        Assert.Equal(3, step!.Before);
        Assert.True(stack.TryPop(out step));
        Assert.Equal(2, step!.Before);
        Assert.False(stack.TryPop(out _));
    }
}
