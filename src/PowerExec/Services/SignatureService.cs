using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace PowerExec.Services;

/// <summary>Prüft die digitale Signatur (Authenticode) von Scripten und Programmen über WinVerifyTrust.</summary>
public static class SignatureService
{
    private static readonly Dictionary<string, (DateTime Written, string Text, bool Valid)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>Lesbarer Status und ob die Signatur gültig ist.</returns>
    public static (string Text, bool Valid) Check(string path)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(path);
            lock (Cache)
                if (Cache.TryGetValue(path, out var c) && c.Written == written) return (c.Text, c.Valid);

            var code = Verify(path);
            string text;
            bool valid = code == 0;
            if (valid)
            {
                var signer = SignerName(path);
                text = signer != null ? $"Signiert von {signer}" : "Gültig signiert";
            }
            else
            {
                text = (uint)code switch
                {
                    0x800B0100 => "Nicht signiert",
                    0x800B0003 => "Nicht signiert",
                    0x80096010 => "Signatur ungültig – Datei wurde nach dem Signieren verändert",
                    0x800B0109 => "Signiert, aber das Zertifikat ist nicht vertrauenswürdig",
                    0x800B0101 => "Signiert, aber das Zertifikat ist abgelaufen",
                    0x800B0111 => "Signatur ausdrücklich nicht vertrauenswürdig",
                    _ => $"Signatur nicht prüfbar (0x{code:X8})"
                };
            }
            lock (Cache) Cache[path] = (written, text, valid);
            return (text, valid);
        }
        catch (Exception ex)
        {
            return ("Signatur nicht prüfbar: " + ex.Message, false);
        }
    }

    private static string? SignerName(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return cert.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch
        {
            return null; // z. B. bei Scripten – Status genügt
        }
    }

    private static int Verify(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path
        };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,           // WTD_UI_NONE
                fdwRevocationChecks = 0,  // WTD_REVOKE_NONE – keine Netzwerkzugriffe
                dwUnionChoice = 1,        // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,
                dwProvFlags = 0x00001000  // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pFile);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
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

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
}
