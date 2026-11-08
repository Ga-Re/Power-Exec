using PowerExec.Core.Execution;

namespace PowerExec.ViewModels;

/// <summary>Was eine Verlaufszeile (<see cref="HistoryRowViewModel"/>) von der App braucht.</summary>
public interface IHistoryHost
{
    /// <summary>Lauf mit denselben Werten wiederholen; <paramref name="onlyFailed"/> = nur fehlgeschlagene Rechner.</summary>
    bool Replay(HistoryEntry entry, bool onlyFailed = false);

    /// <summary>Datei im Explorer markieren.</summary>
    void OpenLocation(string path);
}
