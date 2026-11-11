using PowerExec.Core.Localization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PowerExec.Core.Config;
using PowerExec.Core.Execution;
using PowerExec.UI.Infrastructure;

namespace PowerExec.UI.Services;

/// <summary>
/// Führt ein Script über den Remote-Runner (Windows PowerShell 5.1) auf einem oder mehreren Rechnern aus.
/// Anmeldedaten gehen ausschließlich über stdin an den Runner-Prozess.
/// </summary>
public sealed class RemoteSession : IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private Process? _process;
    private readonly StringBuilder _stderr = new();
    private bool _done;

    public event Action<RemoteResult>? ResultReceived;
    public event Action<string?>? Completed;

    public RemoteSession(IUiDispatcher dispatcher) => _dispatcher = dispatcher;

    public static string WindowsPowerShellPath =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public void Start(ExecutionRequest request, AppSettings settings, RunCredential? credential)
    {
        var psi = new ProcessStartInfo(WindowsPowerShellPath, RemoteRunner.ProcessArguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => OnLine(e.Data);
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (_stderr) _stderr.AppendLine(e.Data); };
        _process.Exited += (_, _) => _dispatcher.Post(OnExited);

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var stdin = _process.StandardInput;
        stdin.WriteLine(RemoteRunner.BuildConfigLine(request, settings, credential != null));
        if (credential != null)
        {
            var account = credential.Domain is null or "." ? credential.UserName : credential.Domain + "\\" + credential.UserName;
            stdin.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(account)));
            WriteSecureBase64(stdin, credential.Password);
        }
        stdin.Flush();
        stdin.Close();
    }

    /// <summary>Schreibt das Kennwort als Base64(UTF-8), ohne einen verwalteten String zu erzeugen.</summary>
    private static void WriteSecureBase64(StreamWriter writer, System.Security.SecureString secure)
    {
        IntPtr ptr = IntPtr.Zero;
        char[]? chars = null;
        byte[]? bytes = null;
        char[]? b64 = null;
        try
        {
            ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
            chars = new char[secure.Length];
            Marshal.Copy(ptr, chars, 0, chars.Length);
            bytes = Encoding.UTF8.GetBytes(chars);
            b64 = new char[((bytes.Length + 2) / 3) * 4];
            Convert.TryToBase64Chars(bytes, b64, out var written);
            writer.Write(b64, 0, written);
            writer.WriteLine();
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            if (chars != null) Array.Clear(chars);
            if (bytes != null) Array.Clear(bytes);
            if (b64 != null) Array.Clear(b64);
        }
    }

    private void OnLine(string? line)
    {
        if (line == null) return;
        if (RemoteRunner.IsDoneLine(line))
        {
            _done = true;
            return;
        }
        var result = RemoteRunner.ParseResultLine(line);
        if (result != null) _dispatcher.Post(() => ResultReceived?.Invoke(result));
    }

    private void OnExited()
    {
        // Restliche Ausgabe abwarten
        try { _process?.WaitForExit(); } catch { }
        string? error = null;
        if (!_done)
        {
            lock (_stderr) error = _stderr.Length > 0 ? _stderr.ToString().Trim() : L.T("The remote runner ended unexpectedly.");
        }
        // Mit niedriger Priorität, damit noch wartende Ergebnisse vorher verarbeitet werden.
        _dispatcher.Post(() => Completed?.Invoke(error));
    }

    public void Cancel()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public void Dispose()
    {
        Cancel();
        _process?.Dispose();
    }
}
