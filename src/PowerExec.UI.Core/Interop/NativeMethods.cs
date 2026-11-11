using System.Runtime.InteropServices;

namespace PowerExec.UI.Interop;

/// <summary>
/// Win32-Aufrufe der Oberflächenlogik. Sie werden nur unter Windows erreicht (die Aufrufer prüfen
/// <see cref="OperatingSystem.IsWindows"/>); die Tests unter Linux berühren sie nicht.
/// </summary>
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    // ── user32: Vordergrundfenster (Vollbild-Erkennung) ──────────────────────────────

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);

    /// <summary>Fensterklasse eines Fensters (leer, wenn nicht ermittelbar).</summary>
    internal static string ClassNameOf(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = GetClassName(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Clamp(length, 0, buffer.Length));
    }

    // ── crypt32: DPAPI für geheime Variablen ─────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct DataBlob { public int Size; public IntPtr Data; }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr LocalFree(IntPtr mem);

    // ── wintrust: Signaturprüfung ────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    internal static partial int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

    // ── mpr: Netzlaufwerke ───────────────────────────────────────────────────────────

    [LibraryImport("mpr.dll", EntryPoint = "WNetGetConnectionW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WNetGetConnection(string localName, [Out] char[] remoteName, ref int length);

    /// <summary>UNC-Pfad hinter einem Laufwerksbuchstaben („Z:“) oder null, wenn es kein Netzlaufwerk ist.</summary>
    internal static string? NetworkPathOf(string drive)
    {
        var buffer = new char[512];
        var length = buffer.Length;
        if (WNetGetConnection(drive, buffer, ref length) != 0) return null;
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }
}
