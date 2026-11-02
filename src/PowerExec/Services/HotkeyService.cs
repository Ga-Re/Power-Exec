using System.Windows.Input;
using System.Windows.Interop;
using PowerExec.Interop;

namespace PowerExec.Services;

/// <summary>Globales Tastenkürzel über RegisterHotKey.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x5045; // "PE"
    private readonly HwndSource _source;
    private bool _registered;

    public event EventHandler? Pressed;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("PowerExecHotkey") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <returns>Fehlermeldung oder null bei Erfolg.</returns>
    public string? Register(string hotkey)
    {
        Unregister();
        if (string.IsNullOrWhiteSpace(hotkey)) return null;
        if (!TryParse(hotkey, out var mods, out var key))
            return $"Tastenkürzel \"{hotkey}\" wird nicht erkannt.";
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, mods | NativeMethods.MOD_NOREPEAT, vk))
            return $"Tastenkürzel \"{hotkey}\" ist bereits von einem anderen Programm belegt.";
        _registered = true;
        return null;
    }

    public void Unregister()
    {
        if (_registered) NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
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
    public static string Display(string hotkey) =>
        string.Join("+", hotkey.Split('+').Select(p => p.Trim() switch
        {
            "Ctrl" => "Strg",
            "Shift" => "Umschalt",
            "Space" => "Leertaste",
            var x => x
        }));

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
