using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ApexClick.Launcher.Interop;

public sealed class HotkeyManager : IDisposable
{
    public const uint MOD_NONE = 0x0000;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;

    public const uint VK_F5 = 0x74;
    public const uint VK_F6 = 0x75;

    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 0xA000;

    public HotkeyManager(Window window)
    {
        var helper = new WindowInteropHelper(window);
        nint handle = helper.Handle != 0 ? helper.Handle : helper.EnsureHandle();
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Не удалось получить HwndSource — регистрация хоткеев невозможна.");
        _source.AddHook(WndProc);
    }

    public int Register(uint modifiers, uint virtualKey, Action onPressed)
    {
        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, modifiers, virtualKey))
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Не удалось зарегистрировать хоткей (vk=0x{virtualKey:X2}, GetLastError={error}) — вероятно, уже занят другим приложением.");
        }
        _handlers[id] = onPressed;
        return id;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToArray()) Unregister(id);
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id))
            UnregisterHotKey(_source.Handle, id);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue((int)wParam, out var onPressed))
        {
            onPressed();
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (int id in _handlers.Keys.ToArray())
            UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
        _source.RemoveHook(WndProc);
    }
}
