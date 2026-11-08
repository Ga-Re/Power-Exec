using System.Security;

namespace PowerExec.Services;

/// <summary>Anmeldedaten für einen einzelnen Start. Werden nie gespeichert und nach Gebrauch verworfen.</summary>
public sealed class RunCredential : IDisposable
{
    public required string Account { get; init; }
    public required string UserName { get; init; }
    public string? Domain { get; init; }
    public required SecureString Password { get; init; }

    public void Dispose() => Password.Dispose();

    public static (string user, string? domain) Split(string account)
    {
        var a = account.Trim();
        var bs = a.IndexOf('\\');
        if (bs > 0) return (a[(bs + 1)..], a[..bs]);
        if (a.Contains('@')) return (a, null);
        return (a, ".");
    }
}
