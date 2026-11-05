using System.Collections.ObjectModel;

namespace PowerExec.Core.Collections;

/// <summary>
/// Gleicht eine <see cref="ObservableCollection{T}"/> mit einer gewünschten Liste ab – mit Remove, Move und Insert
/// statt Clear und Neuaufbau. WPF erzeugt dadurch nur Container für neue Elemente; bleibende Elemente behalten
/// ihren Container (samt Zustand wie Fokus und Hover). Vergleich über Objektidentität.
/// </summary>
public static class CollectionSync
{
    /// <summary>Zählt die Änderungen eines oder mehrerer Abgleiche (für die Leistungsmessung).</summary>
    public sealed class Stats
    {
        public int Inserted { get; set; }
        public int Moved { get; set; }
        public int Removed { get; set; }
        public void Reset() { Inserted = 0; Moved = 0; Removed = 0; }
        public override string ToString() => $"+{Inserted} ↕{Moved} −{Removed}";
    }

    /// <returns>true, wenn sich etwas geändert hat.</returns>
    public static bool SyncTo<T>(ObservableCollection<T> target, IReadOnlyList<T> desired, Stats? stats = null) where T : class
    {
        bool changed = false;
        // 1. Was nicht mehr gewünscht ist, entfernen (von hinten, damit die Indizes stimmen)
        var wanted = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);
        for (int i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i])) { target.RemoveAt(i); changed = true; if (stats != null) stats.Removed++; }

        // 2. Reihenfolge herstellen: vorhandene Elemente verschieben, neue einfügen
        for (int i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], item)) continue;
            int found = -1;
            for (int j = i + 1; j < target.Count; j++)
                if (ReferenceEquals(target[j], item)) { found = j; break; }
            if (found >= 0) { target.Move(found, i); if (stats != null) stats.Moved++; }
            else { target.Insert(i, item); if (stats != null) stats.Inserted++; }
            changed = true;
        }

        // 3. Doppelte in der gewünschten Liste würden hier übrig bleiben – abschneiden
        while (target.Count > desired.Count) { target.RemoveAt(target.Count - 1); changed = true; if (stats != null) stats.Removed++; }
        return changed;
    }
}
