using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ApexClick.Models;
using static ApexClick.Services.Vision.NativeMethods;

namespace ApexClick.Services.Vision;

public sealed class ScreenCaptureService
{
    public enum CaptureMode { FullScreen, SpecificWindow }

    public CaptureMode Mode { get; set; } = CaptureMode.FullScreen;

    public nint? TargetWindowHandle { get; set; }

    public VirtualScreenBounds GetVirtualScreenBounds()
    {
        SetDpiAwareness();
        return new VirtualScreenBounds(
            GetSystemMetrics(SM_XVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_CYVIRTUALSCREEN));
    }

    public bool TryGetTargetWindowRect(out (int Left, int Top, int Width, int Height) rect)
    {
        rect = default;
        if (TargetWindowHandle is not { } hwnd || !IsWindow(hwnd)) return false;
        if (!GetWindowRect(hwnd, out var native)) return false;
        var width = native.Right - native.Left;
        var height = native.Bottom - native.Top;
        if (width <= 0 || height <= 0) return false;
        rect = (native.Left, native.Top, width, height);
        return true;
    }

    public (int Width, int Height) GetPrimaryScreenSize()
    {
        SetDpiAwareness();
        return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    public Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var bitmap = Capture();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }, cancellationToken);

    }

    private Bitmap Capture()
    {
        SetDpiAwareness();

        return Mode switch
        {
            CaptureMode.FullScreen => CaptureVirtualScreen(),
            CaptureMode.SpecificWindow => CaptureWindow(
                TargetWindowHandle ?? throw new InvalidOperationException("TargetWindowHandle не задан для режима SpecificWindow.")),
            _ => throw new ArgumentOutOfRangeException(nameof(Mode)),
        };
    }

    private static Bitmap CaptureVirtualScreen()
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Не удалось определить virtual screen.");

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        nint sourceDc = GetDC(0);
        try
        {
            nint targetDc = graphics.GetHdc();
            try
            {
                if (!BitBlt(targetDc, 0, 0, width, height, sourceDc, left, top, SRCCOPY | CAPTUREBLT))
                    throw new InvalidOperationException($"BitBlt не смог захватить экран (Win32 error {Marshal.GetLastWin32Error()}).");
            }
            finally { graphics.ReleaseHdc(); }
        }
        finally { ReleaseDC(0, sourceDc); }

        return bitmap;
    }

    private static Bitmap CaptureWindow(nint hwnd)
    {
        if (!IsWindow(hwnd))
            throw new ArgumentException("Хендл окна недействителен.", nameof(hwnd));

        if (!GetWindowRect(hwnd, out var rect))
            throw new InvalidOperationException($"GetWindowRect не сработал (Win32 error {Marshal.GetLastWin32Error()}).");

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("У целевого окна нулевая видимая область.");

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        nint hdc = graphics.GetHdc();
        bool printed;
        try { printed = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
        finally { graphics.ReleaseHdc(); }

        if (!printed)
        {
            nint sourceDc = GetWindowDC(hwnd);
            try
            {
                nint targetDc = graphics.GetHdc();
                try
                {
                    if (!BitBlt(targetDc, 0, 0, width, height, sourceDc, 0, 0, SRCCOPY | CAPTUREBLT))
                        throw new InvalidOperationException($"Захват окна не удался (Win32 error {Marshal.GetLastWin32Error()}).");
                }
                finally { graphics.ReleaseHdc(); }
            }
            finally { ReleaseDC(hwnd, sourceDc); }
        }

        return bitmap;
    }
}
