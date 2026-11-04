namespace PowerExec.Core.Config;

/// <summary>Ein Schritt zum Rückgängigmachen: Stand vor der Änderung und kurze Beschreibung („„X“ ausgeblendet“).</summary>
public sealed record UndoStep<T>(T Before, string Description, DateTime TimeUtc);

/// <summary>Begrenzter Stapel der letzten Änderungen (neueste oben).</summary>
public sealed class UndoStack<T>
{
    private readonly LinkedList<UndoStep<T>> _steps = new();

    public UndoStack(int capacity = 10) => Capacity = Math.Max(1, capacity);

    public int Capacity { get; }
    public int Count => _steps.Count;
    public UndoStep<T>? Peek => _steps.First?.Value;

    public void Push(T before, string description, DateTime? nowUtc = null)
    {
        _steps.AddFirst(new UndoStep<T>(before, description, nowUtc ?? DateTime.UtcNow));
        while (_steps.Count > Capacity) _steps.RemoveLast();
    }

    public bool TryPop(out UndoStep<T>? step)
    {
        step = _steps.First?.Value;
        if (step == null) return false;
        _steps.RemoveFirst();
        return true;
    }

    public void Clear() => _steps.Clear();
}
