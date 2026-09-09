using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ApexClick.Models;
using static ApexClick.Services.Capture.NativeMethods;

namespace ApexClick.Services.Capture;

public sealed class InputHookService : IDisposable
{
    private LowLevelHookProc? _keyboardHookProc;
    private LowLevelHookProc? _mouseHookProc;
    private nint _keyboardHookHandle;
    private nint _mouseHookHandle;

    private readonly List<MacroEvent> _events = new();
    private readonly Stopwatch _stopwatch = new();

    public event Action<MacroEvent>? OnInputCaptured;
    public event Action? OnRealUserInputDetected;

    public bool IsRecording { get; private set; }

    public void StartRecording()
    {
        if (IsRecording) return;

        _events.Clear();
        _stopwatch.Restart();

        _keyboardHookProc = KeyboardHookCallback;
        _mouseHookProc = MouseHookCallback;

        nint hModule = GetModuleHandle(null);

        _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, hModule, 0);
        if (_keyboardHookHandle == 0)
            throw new InvalidOperationException($"Не удалось установить клавиатурный хук (Win32 error {Marshal.GetLastWin32Error()}).");

        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, hModule, 0);
        if (_mouseHookHandle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = 0;
            throw new InvalidOperationException($"Не удалось установить хук мыши (Win32 error {error}).");
        }

        IsRecording = true;
    }

    public MacroScript StopRecording()
    {
        if (!IsRecording)
            throw new InvalidOperationException("Запись не была запущена.");

        Unhook();
        IsRecording = false;
        _stopwatch.Stop();

        var script = new MacroScript();
        script.Events.AddRange(_events);

        int width = GetSystemMetrics(SM_CXSCREEN);
        int height = GetSystemMetrics(SM_CYSCREEN);
        script.Metadata["recording.screenWidth"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        script.Metadata["recording.screenHeight"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return script;
    }

    public void Dispose() => Unhook();

    private void Unhook()
    {
        if (_keyboardHookHandle != 0) { UnhookWindowsHookEx(_keyboardHookHandle); _keyboardHookHandle = 0; }
        if (_mouseHookHandle != 0) { UnhookWindowsHookEx(_mouseHookHandle); _mouseHookHandle = 0; }
    }

    private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo != InputSignature.InjectedInputMarker)
                HandleRealKeyEvent((int)wParam, data);
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo != InputSignature.InjectedInputMarker)
                HandleRealMouseEvent((int)wParam, data);
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void HandleRealKeyEvent(int msg, KBDLLHOOKSTRUCT data)
    {
        MacroEventType? type = msg switch
        {
            WM_KEYDOWN or WM_SYSKEYDOWN => MacroEventType.KeyDown,
            WM_KEYUP or WM_SYSKEYUP => MacroEventType.KeyUp,
            _ => null,
        };
        if (type is null) return;

        Emit(new MacroEvent
        {
            TimestampMs = _stopwatch.ElapsedMilliseconds,
            Type = type.Value,
            VirtualKeyCode = (int)data.vkCode,
            ScanCode = (int)data.scanCode,
            KeyboardLayoutId = GetCurrentKeyboardLayoutId(),
        });
    }

    private void HandleRealMouseEvent(int msg, MSLLHOOKSTRUCT data)
    {
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);
        var (x, y) = ScreenCoordinateMath.ToNormalized(data.pt.x, data.pt.y, screenWidth, screenHeight);

        MacroEvent? evt = msg switch
        {
            WM_MOUSEMOVE => NewEvent(MacroEventType.MouseMove, x, y),
            WM_LBUTTONDOWN => NewButtonEvent(MacroEventType.MouseButtonDown, MouseVirtualKeys.Left, x, y),
            WM_LBUTTONUP => NewButtonEvent(MacroEventType.MouseButtonUp, MouseVirtualKeys.Left, x, y),
            WM_RBUTTONDOWN => NewButtonEvent(MacroEventType.MouseButtonDown, MouseVirtualKeys.Right, x, y),
            WM_RBUTTONUP => NewButtonEvent(MacroEventType.MouseButtonUp, MouseVirtualKeys.Right, x, y),
            WM_MBUTTONDOWN => NewButtonEvent(MacroEventType.MouseButtonDown, MouseVirtualKeys.Middle, x, y),
            WM_MBUTTONUP => NewButtonEvent(MacroEventType.MouseButtonUp, MouseVirtualKeys.Middle, x, y),
            
            WM_MOUSEWHEEL => NewEvent(MacroEventType.MouseWheel, x, y, unchecked((short)(data.mouseData >> 16))),
            _ => null,
        };

        if (evt is not null) Emit(evt);
    }

    private void Emit(MacroEvent evt)
    {
        if (IsRecording)
        {
            _events.Add(evt);
            OnInputCaptured?.Invoke(evt);
        }

        OnRealUserInputDetected?.Invoke();
    }

    private MacroEvent NewEvent(MacroEventType type, double x, double y, object? payload = null) => new()
    {
        TimestampMs = _stopwatch.ElapsedMilliseconds,
        Type = type,
        NormalizedX = x,
        NormalizedY = y,
        Payload = payload,
    };

    private MacroEvent NewButtonEvent(MacroEventType type, int virtualKey, double x, double y) => new()
    {
        TimestampMs = _stopwatch.ElapsedMilliseconds,
        Type = type,
        VirtualKeyCode = virtualKey,
        NormalizedX = x,
        NormalizedY = y,
    };

    private static string? GetCurrentKeyboardLayoutId()
    {
        try
        {
            uint threadId = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            int langId = (int)((long)GetKeyboardLayout(threadId) & 0xFFFF);
            return langId == 0 ? null : new CultureInfo(langId).Name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
