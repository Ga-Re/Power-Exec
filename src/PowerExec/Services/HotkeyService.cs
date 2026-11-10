using PowerExec.Core.Localization;
using System.Windows.Input;
using System.Windows.Interop;
using PowerExec.Interop;

namespace PowerExec.Services;

/// <summary>Globales Tastenkürzel über RegisterHotKey.</summary>
public sealed class HotkeyService : IHotkeys
{
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = new();

    /// <summary>Ein registriertes Tastenkürzel wurde gedrückt (Parameter: Kennung).</summary>
    public event Action<int>? Pressed;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("PowerExecHotkey") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registriert ein Tastenkürzel unter der Kennung <paramref name="id"/>.</summary>
    /// <returns>Fehlermeldung oder null bei Erfolg.</returns>
    public string? Register(int id, string hotkey)
    {
        Unregister(id);
        if (string.IsNullOrWhiteSpace(hotkey)) return null;
        if (!TryParse(hotkey, out var mods, out var key))
            return L.F("Tastenkürzel \"{0}\" wird nicht erkannt.", hotkey);
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, mods | NativeMethods.MOD_NOREPEAT, vk))
            return L.F("Tastenkürzel \"{0}\" ist bereits belegt (anderes Programm oder anderes Panel).", hotkey);
        _registered.Add(id);
        return null;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) NativeMethods.UnregisterHotKey(_source.Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.ToList()) Unregister(id);
    }

    public static bool TryParse(string text, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "strg" or "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                case "shift" or "umschalt": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "win" or "windows": modifiers |= NativeMethods.MOD_WIN; break;
                case "space" or "leer" or "leertaste": key = Key.Space; break;
                default:
                    if (Enum.TryParse<Key>(raw, true, out var k)) key = k;
                    else if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else return false;
                    break;
            }
        }
        return key != Key.None && modifiers != 0;
    }

    /// <summary>Formatiert ein Tastenkürzel aus einem Tastendruck, z. B. "Ctrl+Alt+Space".</summary>
    public static string? Format(ModifierKeys mods, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None) return null;
        if (mods == ModifierKeys.None) return null;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Anzeige auf Deutsch: "Strg+Alt+Leertaste".</summary>
    public static string Display(string hotkey) => Infrastructure.DisplayText.Hotkey(hotkey);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _registered.Contains(wParam.ToInt32()))
        {
            Pressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
