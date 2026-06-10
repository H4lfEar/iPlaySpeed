using System.Windows.Interop;

namespace IPlaySpeed.App.Native;

/// <summary>
/// 전역 단축키 등록기. 메시지 전용 창(message-only window)을 만들어 WM_HOTKEY를 받는다.
/// 게임이 포그라운드여도 단축키가 동작하도록 시스템 전역으로 등록된다.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int HWND_MESSAGE = -3;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId = 1;

    public GlobalHotkey()
    {
        var parameters = new HwndSourceParameters("IPlaySpeedHotkeyWindow")
        {
            ParentWindow = new IntPtr(HWND_MESSAGE), // 메시지 전용 창
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>"Ctrl+Alt+I" 같은 문자열을 파싱해 단축키를 등록한다. 실패하면 false.</summary>
    public bool Register(string gesture, Action callback)
    {
        if (!TryParse(gesture, out uint mods, out uint vk))
            return false;

        int id = _nextId++;
        // 같은 키 반복 입력 무시(MOD_NOREPEAT)로 한 번만 발동.
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, mods | NativeMethods.MOD_NOREPEAT, vk))
            return false;

        _callbacks[id] = callback;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_callbacks.TryGetValue(id, out var cb))
            {
                cb();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>"Ctrl+Alt+I" → modifiers + virtual key. 지원: Ctrl/Alt/Shift/Win + A~Z, 0~9, F1~F12.</summary>
    public static bool TryParse(string gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(gesture))
            return false;

        foreach (string raw in gesture.Split('+'))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL; break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT; break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT; break;
                case "win":
                    modifiers |= NativeMethods.MOD_WIN; break;
                default:
                    if (!TryParseKey(part, out vk))
                        return false;
                    break;
            }
        }
        return vk != 0 && modifiers != 0;
    }

    private static bool TryParseKey(string key, out uint vk)
    {
        vk = 0;
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z') { vk = c; return true; }       // VK 'A'..'Z' == ASCII
            if (c is >= '0' and <= '9') { vk = c; return true; }       // VK '0'..'9' == ASCII
        }
        if (key.Length >= 2 && (key[0] == 'F' || key[0] == 'f')
            && int.TryParse(key.AsSpan(1), out int n) && n is >= 1 and <= 12)
        {
            vk = (uint)(0x70 + (n - 1)); // VK_F1 = 0x70
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        foreach (int id in _callbacks.Keys)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _callbacks.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
