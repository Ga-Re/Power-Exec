using PowerExec.Core.Localization;
using System.Text;
using System.Windows;
using PowerExec.Core.Config;
using PowerExec.UI.Services;

namespace PowerExec.Desktop.Services;

/// <summary>Liest, was aus dem Explorer (Dateien, Ordner) oder dem Browser (Links) ins Panel gezogen wurde.</summary>
public static class ExternalDrop
{
    private const string MozUrl = "text/x-moz-url";
    private const string UrlW = "UniformResourceLocatorW";
    private const string Url = "UniformResourceLocator";
    private const string FileGroupDescriptorW = "FileGroupDescriptorW";

    public static bool HasData(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(UrlW) || data.GetDataPresent(Url) ||
        data.GetDataPresent(MozUrl) || (data.GetDataPresent(DataFormats.UnicodeText) && DropClassifier.FromUrl(Text(data)) != null);

    public static IReadOnlyList<DroppedTarget> Read(IDataObject data)
    {
        var result = new List<DroppedTarget>();
        try
        {
            if (data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                foreach (var f in files)
                    result.Add(DropClassifier.Classify(f, Directory.Exists, p => { try { return File.ReadAllText(p); } catch { return null; } }));
                return result;
            }

            // Firefox: „URL\nTitel“
            if (ReadString(data, MozUrl, Encoding.Unicode) is { } moz)
            {
                var parts = moz.Replace("\r", "").Split('\n');
                if (DropClassifier.FromUrl(parts[0], parts.Length > 1 ? parts[1] : null) is { } t) { result.Add(t); return result; }
            }
            // Chrome/Edge: URL plus Dateiname „Seitentitel.url“ im FileGroupDescriptor
            var url = ReadString(data, UrlW, Encoding.Unicode) ?? ReadString(data, Url, Encoding.Default) ?? Text(data);
            if (DropClassifier.FromUrl(url, TitleFromDescriptor(data)) is { } link) result.Add(link);
        }
        catch (Exception ex)
        {
            AppLog.Error("Drag & drop couldn't be read", ex);
        }
        return result;
    }

    private static string? Text(IDataObject data)
    {
        try { return data.GetData(DataFormats.UnicodeText) as string; } catch { return null; }
    }

    private static string? ReadString(IDataObject data, string format, Encoding encoding)
    {
        if (!data.GetDataPresent(format)) return null;
        var value = data.GetData(format);
        return value switch
        {
            string s => s.TrimEnd('\0'),
            MemoryStream ms => encoding.GetString(ms.ToArray()).TrimEnd('\0'),
            _ => null
        };
    }

    /// <summary>FILEGROUPDESCRIPTORW: Anzahl (4 Byte), dann FILEDESCRIPTORW mit cFileName ab Byte 72.</summary>
    private static string? TitleFromDescriptor(IDataObject data)
    {
        try
        {
            if (data.GetData(FileGroupDescriptorW) is not MemoryStream ms) return null;
            var bytes = ms.ToArray();
            const int nameOffset = 4 + 72;
            if (bytes.Length < nameOffset + 2) return null;
            var name = Encoding.Unicode.GetString(bytes, nameOffset, Math.Min(520, bytes.Length - nameOffset));
            var end = name.IndexOf('\0');
            if (end >= 0) name = name[..end];
            return name.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }
        catch
        {
            return null;
        }
    }
}
