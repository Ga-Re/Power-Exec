using PowerExec.Core.Models;

namespace PowerExec.Core.Search;

public enum TypeFilter { All, Scripts, Programs, Favorites }

/// <summary>Suche und Filter für das Panel.</summary>
public static class ItemFilter
{
    public static bool MatchesType(LaunchItem item, TypeFilter filter, bool isFavorite) => filter switch
    {
        TypeFilter.Scripts => item.Type is ItemType.PowerShell or ItemType.Batch,
        TypeFilter.Programs => item.Type is ItemType.Executable or ItemType.Shortcut or ItemType.ConsoleSnapIn or ItemType.Other,
        TypeFilter.Favorites => isFavorite,
        _ => true
    };

    /// <summary>Alle Suchbegriffe (durch Leerzeichen getrennt) müssen vorkommen.</summary>
    public static bool MatchesQuery(LaunchItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var text = item.SearchText;
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
        return true;
    }

    /// <summary>Treffer im Namen zuerst, danach alphabetisch.</summary>
    public static int Rank(LaunchItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        var q = query.Trim();
        if (item.DisplayName.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) return 0;
        if (item.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase)) return 1;
        return 2;
    }
}
