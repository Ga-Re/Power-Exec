using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace PowerExec.Services;

/// <summary>Text in die Zwischenablage – optional formatiert (HTML) und zusätzlich als reiner Text.</summary>
public static class ClipboardText
{
    public static void Set(string text, bool asHtml)
    {
        if (!asHtml)
        {
            Clipboard.SetText(text);
            return;
        }
        var data = new DataObject();
        data.SetData(DataFormats.Html, ToCfHtml(text));
        data.SetData(DataFormats.UnicodeText, ToPlain(text));
        Clipboard.SetDataObject(data, copy: true);
    }

    /// <summary>HTML grob in reinen Text umwandeln (Zeilenumbrüche erhalten).</summary>
    public static string ToPlain(string html)
    {
        var t = Regex.Replace(html, @"<\s*br\s*/?>|</\s*(p|div|li|tr|h\d)\s*>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, "<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(t).Trim();
    }

    /// <summary>CF_HTML-Format (Header mit Byte-Offsets in UTF-8).</summary>
    public static string ToCfHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string pre = "<html><body><!--StartFragment-->";
        const string post = "<!--EndFragment--></body></html>";
        int headerLen = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int startHtml = headerLen;
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(pre);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(post);
        return string.Format(header, startHtml, endHtml, startFragment, endFragment) + pre + fragment + post;
    }
}
