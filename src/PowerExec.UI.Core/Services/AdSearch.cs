using System.DirectoryServices;

namespace PowerExec.UI.Services;

/// <summary>Sucht Computerkonten im Active Directory (mit dem angemeldeten Konto, ohne gespeicherte Anmeldedaten).</summary>
public static class AdSearch
{
    public const int MaxResults = 5000;

    /// <summary>
    /// Distinguished Names der Domäne, aller Organisationseinheiten und des Containers „Computers“ – für die Auswahl
    /// „Ab OU“ als Baum.
    /// </summary>
    public static Task<IReadOnlyList<string>> FindOrganizationalUnitsAsync(CancellationToken token = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            using var root = new DirectoryEntry();
            var result = new List<string>();
            if (root.Properties["distinguishedName"] is { Count: > 0 } d && d[0] is string domain) result.Add(domain);
            using var searcher = new DirectorySearcher(root, "(|(objectCategory=organizationalUnit)(&(objectCategory=container)(cn=Computers)))",
                new[] { "distinguishedName" })
            { PageSize = 500, SizeLimit = MaxResults, SearchScope = SearchScope.Subtree };
            using var results = searcher.FindAll();
            foreach (SearchResult r in results)
            {
                token.ThrowIfCancellationRequested();
                if (r.Properties["distinguishedName"] is { Count: > 0 } p && p[0] is string dn) result.Add(dn);
            }
            return result;
        }, token);

    /// <summary>
    /// Eingabe: Name mit Platzhaltern (z. B. „SRV-*“), eine OU („OU=Server,DC=firma,DC=local“)
    /// oder ein eigener LDAP-Filter in Klammern („(operatingSystem=*Server*)“).
    /// </summary>
    public static Task<IReadOnlyList<string>> FindComputersAsync(string query, CancellationToken token = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            query = query.Trim();
            if (query.Length == 0) return Array.Empty<string>();
            DirectoryEntry root;
            string filter;
            if (query.Contains("OU=", StringComparison.OrdinalIgnoreCase) || query.Contains("DC=", StringComparison.OrdinalIgnoreCase))
            {
                root = new DirectoryEntry("LDAP://" + query);
                filter = "(objectCategory=computer)";
            }
            else
            {
                root = new DirectoryEntry();
                filter = query.StartsWith('(')
                    ? $"(&(objectCategory=computer){query})"
                    : $"(&(objectCategory=computer)(name={Escape(query)}))";
            }
            using (root)
            using (var searcher = new DirectorySearcher(root, filter, new[] { "name", "dNSHostName" })
            { PageSize = 500, SizeLimit = MaxResults, SearchScope = SearchScope.Subtree })
            using (var results = searcher.FindAll())
            {
                var names = new List<string>();
                foreach (SearchResult r in results)
                {
                    token.ThrowIfCancellationRequested();
                    if (r.Properties["name"] is { Count: > 0 } p && p[0] is string n) names.Add(n.ToUpperInvariant());
                }
                return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }, token);

    /// <summary>Computer nach fertigem LDAP-Filter (dynamische Rechnergruppen), optional ab einer OU.</summary>
    public static Task<IReadOnlyList<string>> QueryAsync(string ldapFilter, string? searchBase, CancellationToken token = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            using var root = string.IsNullOrWhiteSpace(searchBase) ? new DirectoryEntry() : new DirectoryEntry("LDAP://" + searchBase.Trim());
            using var searcher = new DirectorySearcher(root, ldapFilter, new[] { "name" })
            { PageSize = 500, SizeLimit = MaxResults, SearchScope = SearchScope.Subtree };
            using var results = searcher.FindAll();
            var names = new List<string>();
            foreach (SearchResult r in results)
            {
                token.ThrowIfCancellationRequested();
                if (r.Properties["name"] is { Count: > 0 } p && p[0] is string n) names.Add(n.ToUpperInvariant());
            }
            return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }, token);

    /// <summary>Maskiert LDAP-Sonderzeichen, lässt aber * als Platzhalter stehen.</summary>
    private static string Escape(string s) =>
        s.Replace("\\", "\\5c").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
}
