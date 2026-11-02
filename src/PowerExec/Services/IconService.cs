using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PowerExec.Services;

/// <summary>
/// Lädt Programmsymbole (EXE, Verknüpfungen, Snap-ins) über die Windows-Shell.
/// Läuft auf einem eigenen STA-Thread, damit langsame Netzwerkpfade das Panel nicht blockieren.
/// </summary>
public sealed class IconService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Action<ImageSource>>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<string> _queue = new();
    private readonly Thread _thread;

    public IconService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _thread = new Thread(Worker) { IsBackground = true, Name = "PowerExec.Icons" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Liefert das Symbol sofort aus dem Cache oder später über <paramref name="onLoaded"/> (UI-Thread).</summary>
    public ImageSource? Get(string path, DateTime lastWriteUtc, Action<ImageSource> onLoaded)
    {
        var key = path + "|" + lastWriteUtc.Ticks;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        bool first = false;
        var list = _pending.GetOrAdd(key, _ => { first = true; return new List<Action<ImageSource>>(); });
        lock (list) list.Add(onLoaded);
        if (first && !_queue.IsAddingCompleted) _queue.Add(key);
        return null;
    }

    private void Worker()
    {
        foreach (var key in _queue.GetConsumingEnumerable())
        {
            var path = key[..key.LastIndexOf('|')];
            ImageSource? image = null;
            try { image = Load(path); } catch { }
            _cache[key] = image;

            if (_pending.TryRemove(key, out var callbacks) && image != null)
            {
                Action<ImageSource>[] copy;
                lock (callbacks) copy = callbacks.ToArray();
                _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    foreach (var cb in copy) cb(image);
                });
            }
        }
    }

    private static ImageSource? Load(string path)
    {
        var info = new SHFILEINFO();
        // Großes Symbol (32 px), ohne Verknüpfungspfeil
        var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    public void Dispose() => _queue.CompleteAdding();

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
