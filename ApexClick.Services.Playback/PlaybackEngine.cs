using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexClick.Models;
using ApexClick.Services.Capture;
using static ApexClick.Services.Playback.NativeMethods;

namespace ApexClick.Services.Playback;

public sealed class PlaybackEngine
{
    private static readonly TimeSpan SmartPauseDuration = TimeSpan.FromSeconds(3);
    
    private static readonly TimeSpan MaxContinuousPause = TimeSpan.FromSeconds(15);

    private readonly PlaybackTimingService _timingService;
    private readonly InputHookService _inputHookService;
    private DateTime? _pausedUntilUtc;
    private DateTime? _pauseChainStartUtc;
    private bool _manualPause;

    public PlaybackEngine(PlaybackTimingService timingService, InputHookService inputHookService)
    {
        _timingService = timingService;
        _inputHookService = inputHookService;
    }

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    public async Task PlayAsync(MacroScript script, CancellationToken cancellationToken)
    {
        if (IsPlaying)
            throw new InvalidOperationException("Воспроизведение уже выполняется.");

        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);

        IsPlaying = true;
        IsPaused = false;
        _manualPause = false;
        _pausedUntilUtc = null;
        _inputHookService.OnRealUserInputDetected += OnRealUserInputDuringPlayback;

        bool timerPeriodRaised = timeBeginPeriod(1) == 0;

        try
        {
            for (int i = 0; i < script.Events.Count; i++)
            {
                var evt = script.Events[i];
                cancellationToken.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (evt.Type == MacroEventType.Wait)
                {
                    int waitMs = PayloadConvert.TryGetInt32(evt.Payload) ?? 0;
                    if (waitMs > 0) await PreciseDelayAsync(TimeSpan.FromMilliseconds(waitMs), cancellationToken);
                    continue;
                }

                ExecuteEvent(evt, string.Equals(script.Metadata.GetValueOrDefault("recording.coordinateSpace"), "virtual-screen", StringComparison.OrdinalIgnoreCase));

                long? gapToNext = i + 1 < script.Events.Count ? script.Events[i + 1].TimestampMs - evt.TimestampMs : null;
                var delay = _timingService.GetInterActionDelay(script, gapToNext);
                if (delay > TimeSpan.Zero) await PreciseDelayAsync(delay, cancellationToken);
            }
        }
        finally
        {
            if (timerPeriodRaised) timeEndPeriod(1);
            _inputHookService.OnRealUserInputDetected -= OnRealUserInputDuringPlayback;
            IsPlaying = false;
            IsPaused = false;
            _manualPause = false;
            _pausedUntilUtc = null;
            _pauseChainStartUtc = null;
        }
    }

    public void Pause(TimeSpan duration)
    {
        if (_manualPause) return;
        var now = DateTime.UtcNow;
        _pauseChainStartUtc ??= now;
        var requestedUntil = now + duration;
        var chainCeiling = _pauseChainStartUtc.Value + MaxContinuousPause;
        _pausedUntilUtc = requestedUntil < chainCeiling ? requestedUntil : chainCeiling;
        IsPaused = true;
    }

    public void PauseManual()
    {
        _manualPause = true;
        _pausedUntilUtc = null;
        _pauseChainStartUtc = null;
        IsPaused = true;
    }

    public void ResumeManual()
    {
        _manualPause = false;
        _pausedUntilUtc = null;
        _pauseChainStartUtc = null;
        IsPaused = false;
    }

    public async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (IsPaused)
        {
            if (!_manualPause && _pausedUntilUtc is { } until && DateTime.UtcNow >= until)
            {
                IsPaused = false;
                _pausedUntilUtc = null;
                _pauseChainStartUtc = null;
                break;
            }
            await Task.Delay(50, cancellationToken);
        }
    }

    private static async Task PreciseDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero) return;

        const int spinTailMs = 2;
        var sw = Stopwatch.StartNew();
        var coarse = delay - TimeSpan.FromMilliseconds(spinTailMs);
        if (coarse > TimeSpan.Zero)
            await Task.Delay(coarse, cancellationToken);

        while (sw.Elapsed < delay)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.SpinWait(200);
        }
    }

    private void OnRealUserInputDuringPlayback()
    {
        if (IsPlaying) Pause(SmartPauseDuration);
    }

    private static void ExecuteEvent(MacroEvent evt, bool useVirtualDesktop)
    {
        switch (evt.Type)
        {
            case MacroEventType.MouseMove: SendMouseMove(evt, useVirtualDesktop); break;
            case MacroEventType.MouseButtonDown: SendMouseButton(evt, isDown: true, useVirtualDesktop); break;
            case MacroEventType.MouseButtonUp: SendMouseButton(evt, isDown: false, useVirtualDesktop); break;
            case MacroEventType.MouseWheel: SendMouseWheel(evt); break;
            case MacroEventType.KeyDown: SendKey(evt, isKeyUp: false); break;
            case MacroEventType.KeyUp: SendKey(evt, isKeyUp: true); break;
            case MacroEventType.TextInput: SendText(evt); break;
            case MacroEventType.ConditionScreenTemplate:
            case MacroEventType.ConditionScreenPixel:
            case MacroEventType.ConditionOcrText:
            case MacroEventType.Loop:
            case MacroEventType.Variable:
            case MacroEventType.SubScenarioCall:
                throw new NotSupportedException(
                    $"Узел {evt.Type} требует движка графа (Фаза 3) и/или Vision (Фаза 4-5) — не поддерживается линейным PlaybackEngine из Фазы 1.");
            default:
                throw new NotSupportedException($"Неизвестный тип события: {evt.Type}");
        }
    }

    private static void SendMouseMove(MacroEvent evt, bool useVirtualDesktop)
    {
        if (evt.NormalizedX is not { } x || evt.NormalizedY is not { } y) return;
        var (absX, absY) = useVirtualDesktop
            ? ScreenCoordinateMath.ToSendInputVirtualDesktopAbsoluteRange(x, y)
            : ScreenCoordinateMath.ToSendInputAbsoluteRange(x, y);

        Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | (useVirtualDesktop ? MOUSEEVENTF_VIRTUALDESK : 0u),
                    dwExtraInfo = InputSignature.InjectedInputMarker,
                },
            },
        });
    }

    private static void SendMouseButton(MacroEvent evt, bool isDown, bool useVirtualDesktop)
    {
        uint flag = (evt.VirtualKeyCode, isDown) switch
        {
            (MouseVirtualKeys.Left, true) => MOUSEEVENTF_LEFTDOWN,
            (MouseVirtualKeys.Left, false) => MOUSEEVENTF_LEFTUP,
            (MouseVirtualKeys.Right, true) => MOUSEEVENTF_RIGHTDOWN,
            (MouseVirtualKeys.Right, false) => MOUSEEVENTF_RIGHTUP,
            (MouseVirtualKeys.Middle, true) => MOUSEEVENTF_MIDDLEDOWN,
            (MouseVirtualKeys.Middle, false) => MOUSEEVENTF_MIDDLEUP,
            _ => 0u,
        };
        if (flag == 0u) return;

        var mi = new MOUSEINPUT { dwFlags = flag, dwExtraInfo = InputSignature.InjectedInputMarker };

        if (evt.NormalizedX is { } x && evt.NormalizedY is { } y)
        {
            var (absX, absY) = useVirtualDesktop
                ? ScreenCoordinateMath.ToSendInputVirtualDesktopAbsoluteRange(x, y)
                : ScreenCoordinateMath.ToSendInputAbsoluteRange(x, y);
            mi.dx = absX;
            mi.dy = absY;
            mi.dwFlags |= MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | (useVirtualDesktop ? MOUSEEVENTF_VIRTUALDESK : 0u);
        }

        Send(new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = mi } });
    }

    private static void SendMouseWheel(MacroEvent evt)
    {
        int delta = PayloadConvert.TryGetInt32(evt.Payload) ?? 0;
        if (delta == 0) return;

        Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)delta),
                    dwFlags = MOUSEEVENTF_WHEEL,
                    dwExtraInfo = InputSignature.InjectedInputMarker,
                },
            },
        });
    }

    private static void SendText(MacroEvent evt)
    {
        var text = evt.Payload?.ToString();
        if (string.IsNullOrEmpty(text)) return;

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            ushort code = ch;
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = code,
                        dwFlags = KEYEVENTF_UNICODE,
                        dwExtraInfo = InputSignature.InjectedInputMarker
                    }
                }
            });
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = code,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        dwExtraInfo = InputSignature.InjectedInputMarker
                    }
                }
            });
        }

        if (SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>()) != inputs.Count)
            throw new InvalidOperationException($"SendInput не смог отправить текст (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    private static void SendKey(MacroEvent evt, bool isKeyUp)
    {
        
        ushort scanCode = evt.ScanCode is { } sc ? (ushort)sc : evt.VirtualKeyCode is { } vk ? (ushort)vk : (ushort)0;
        if (scanCode == 0) return;

        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = scanCode,
                    dwFlags = KEYEVENTF_SCANCODE | (isKeyUp ? KEYEVENTF_KEYUP : 0u),
                    dwExtraInfo = InputSignature.InjectedInputMarker,
                },
            },
        });

    }

    private static void Send(INPUT input)
    {
        uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent != 1)
            throw new InvalidOperationException($"SendInput не смог отправить событие (Win32 error {Marshal.GetLastWin32Error()}).");
    }
}
