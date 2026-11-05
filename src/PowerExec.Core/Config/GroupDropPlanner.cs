namespace PowerExec.Core.Config;

/// <summary>Bereich auf dem Kopf einer Gruppe beim Ziehen: obere Kante, Mitte, untere Kante.</summary>
public enum GroupDropZone { Before, Into, After }

/// <summary>Ergebnis der Planung: wohin eine gezogene Gruppe beim Loslassen käme.</summary>
public sealed record GroupDropPlan
{
    public bool IsValid { get; init; }
    /// <summary>Gültig, aber die Gruppe bliebe, wo sie ist.</summary>
    public bool IsNoOp { get; init; }
    public GroupDropPosition Position { get; init; }
    /// <summary>Gruppe, vor/hinter/in die eingefügt wird.</summary>
    public string Target { get; init; } = string.Empty;
    /// <summary>Neue übergeordnete Gruppe (null = oberste Ebene).</summary>
    public string? NewParent { get; init; }
    /// <summary>Ebene, auf der die Gruppe landet (0 = oberste).</summary>
    public int Depth { get; init; }
    /// <summary>Spanne der Ebenen, die an dieser Stelle durch seitliches Ziehen wählbar sind.</summary>
    public int MinDepth { get; init; }
    public int MaxDepth { get; init; }
    /// <summary>Warum hier nicht abgelegt werden kann (nur bei ungültigem Plan).</summary>
    public string? Reason { get; init; }

    public bool CanChooseDepth => MaxDepth > MinDepth;
}

/// <summary>
/// Plant das Ablegen einer gezogenen Gruppe (Panel und Einstellungen): aus der Gruppe unter dem Mauszeiger, dem Bereich
/// auf ihrem Kopf und dem seitlichen Versatz (in Ebenen) wird Ziel und Ebene. Zwischen zwei Gruppen ist die Ebene frei
/// wählbar, soweit die Nachbarn es zulassen: nach rechts tiefer, nach links höher.
/// </summary>
public static class GroupDropPlanner
{
    /// <param name="visibleOrder">Sichtbare eigene Gruppen in Anzeigereihenfolge (Pfade), einschließlich der gezogenen.</param>
    /// <param name="dragged">Pfad der gezogenen Gruppe.</param>
    /// <param name="hovered">Gruppe unter dem Mauszeiger.</param>
    /// <param name="levelOffset">Seitlicher Versatz seit Beginn des Ziehens in Ebenen (positiv = nach rechts).</param>
    /// <param name="siblingsOnly">Nur die Reihenfolge unter Geschwistern ändern (Einstellungen).</param>
    /// <param name="exists">Gibt es eine Gruppe mit diesem Pfad? (Namensgleichheit am Ziel verhindern)</param>
    public static GroupDropPlan Plan(IReadOnlyList<string> visibleOrder, string dragged, string hovered, GroupDropZone zone,
        int levelOffset, bool siblingsOnly, Func<string, bool>? exists = null)
    {
        dragged = GroupManager.Normalize(dragged);
        hovered = GroupManager.Normalize(hovered);
        var draggedDepth = GroupManager.DepthOf(dragged);
        var draggedParent = GroupManager.ParentOf(dragged);
        var leaf = GroupManager.LeafOf(dragged);

        // Über der gezogenen Gruppe selbst (oder ihren Untergruppen): bleibt, wo sie ist
        if (GroupManager.IsSameOrBelow(hovered, dragged))
            return new GroupDropPlan { IsValid = true, IsNoOp = true, Position = GroupDropPosition.Before, Target = dragged, NewParent = draggedParent, Depth = draggedDepth, MinDepth = draggedDepth, MaxDepth = draggedDepth };

        var rest = visibleOrder.Select(GroupManager.Normalize).Where(p => !GroupManager.IsSameOrBelow(p, dragged)).ToList();
        int i = rest.FindIndex(p => p.Equals(hovered, StringComparison.CurrentCultureIgnoreCase));
        if (i < 0) return Invalid("Hier kann keine Gruppe abgelegt werden.");

        GroupDropPosition position;
        string target;
        int depth, min, max;
        if (zone == GroupDropZone.Into)
        {
            if (siblingsOnly) return Invalid("Hier lässt sich nur die Reihenfolge ändern. Untergruppen legst du im Panel an, indem du eine Gruppe auf die Mitte einer anderen ziehst.");
            position = GroupDropPosition.Into;
            target = rest[i];
            depth = min = max = GroupManager.DepthOf(target) + 1;
        }
        else
        {
            var prev = zone == GroupDropZone.Before ? (i > 0 ? rest[i - 1] : null) : rest[i];
            var next = zone == GroupDropZone.Before ? rest[i] : (i + 1 < rest.Count ? rest[i + 1] : null);
            int prevDepth = prev == null ? 0 : GroupManager.DepthOf(prev);
            int nextDepth = next == null ? 0 : GroupManager.DepthOf(next);
            min = next == null ? 0 : nextDepth;
            max = prev == null ? 0 : prevDepth;
            if (prev != null && next != null && nextDepth > prevDepth) min = max = nextDepth; // erstes Kind von „prev“
            if (min > max) min = max;
            depth = Math.Clamp(draggedDepth + levelOffset, min, max);
            if (siblingsOnly && draggedDepth >= min && draggedDepth <= max) depth = draggedDepth;

            if (next != null && nextDepth == depth)
            {
                position = GroupDropPosition.Before;
                target = next;
            }
            else
            {
                var anc = prev!;
                while (GroupManager.DepthOf(anc) > depth) anc = GroupManager.ParentOf(anc)!;
                position = GroupDropPosition.After;
                target = anc;
            }
        }

        var newParent = position == GroupDropPosition.Into ? target : GroupManager.ParentOf(target);
        var plan = new GroupDropPlan
        {
            IsValid = true, Position = position, Target = target, NewParent = newParent, Depth = depth, MinDepth = min, MaxDepth = max
        };

        bool sameParent = string.Equals(newParent, draggedParent, StringComparison.CurrentCultureIgnoreCase);
        if (siblingsOnly && !sameParent)
            return plan with
            {
                IsValid = false,
                Reason = draggedParent == null
                    ? $"„{leaf}“ bleibt auf der obersten Ebene – hier lässt sich nur die Reihenfolge ändern."
                    : $"„{leaf}“ bleibt in „{GroupManager.LeafOf(draggedParent)}“ – hier lässt sich nur die Reihenfolge ändern."
            };

        var newPath = newParent == null ? leaf : newParent + GroupManager.Separator + leaf;
        if (!sameParent && exists != null && exists(newPath))
            return plan with
            {
                IsValid = false,
                Reason = newParent == null
                    ? $"Auf der obersten Ebene gibt es schon eine Gruppe „{leaf}“."
                    : $"In „{GroupManager.LeafOf(newParent)}“ gibt es schon eine Gruppe „{leaf}“."
            };

        return plan with { IsNoOp = sameParent && IsSameOrder(visibleOrder, dragged, position, target) };
    }

    /// <summary>Kurzer Satz für Hinweise: was beim Loslassen passiert.</summary>
    public static string Describe(GroupDropPlan plan, string dragged)
    {
        var name = $"„{GroupManager.LeafOf(GroupManager.Normalize(dragged))}“";
        if (!plan.IsValid) return plan.Reason ?? "Hier nicht möglich.";
        if (plan.IsNoOp) return "Keine Änderung.";
        var target = $"„{GroupManager.LeafOf(plan.Target)}“";
        if (plan.Position == GroupDropPosition.Into) return $"{name} wird Untergruppe von {target}.";
        var where = plan.NewParent == null ? "oberste Ebene" : $"in „{GroupManager.LeafOf(plan.NewParent)}“";
        return $"{name}: {where}, {(plan.Position == GroupDropPosition.Before ? "vor" : "nach")} {target}";
    }

    private static GroupDropPlan Invalid(string reason) => new() { IsValid = false, Reason = reason };

    /// <summary>Ergäbe das Verschieben (bei gleicher übergeordneter Gruppe) dieselbe Reihenfolge?</summary>
    private static bool IsSameOrder(IReadOnlyList<string> order, string dragged, GroupDropPosition position, string target)
    {
        var all = order.Select(GroupManager.Normalize).ToList();
        var subtree = all.Where(p => GroupManager.IsSameOrBelow(p, dragged)).ToList();
        var rest = all.Where(p => !GroupManager.IsSameOrBelow(p, dragged)).ToList();
        int index = position == GroupDropPosition.Before
            ? rest.FindIndex(p => p.Equals(target, StringComparison.CurrentCultureIgnoreCase))
            : rest.FindLastIndex(p => GroupManager.IsSameOrBelow(p, target)) + 1;
        if (index < 0) index = rest.Count;
        rest.InsertRange(index, subtree);
        return rest.SequenceEqual(all, StringComparer.CurrentCultureIgnoreCase);
    }
}
