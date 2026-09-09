using System.Runtime.InteropServices;
using ApexClick.Models;
using OpenCvSharp;
using Tesseract;
using static ApexClick.Services.Vision.NativeMethods;

namespace ApexClick.Services.Vision;

public sealed class ImageAnalysisService : IDisposable
{
    private readonly object _ocrGate = new();
    private TesseractEngine? _ocrEngine;
    private string? _ocrEngineLanguage;

    public string? TessdataPath { get; set; }

    public string OcrLanguage { get; set; } = "eng";

    public Task<(bool Found, double Confidence, NormalizedRegion? Location)> FindTemplateInWindowAsync(
        byte[] frame, string templateImagePath, double confidenceThreshold,
        (int Left, int Top, int Width, int Height) windowRect, VirtualScreenBounds virtualScreen,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var local = await FindTemplateAsync(frame, templateImagePath, confidenceThreshold, cancellationToken);
            if (!local.Found || local.Location is null) return local;

            var r = local.Location.Value;
            var absX = windowRect.Left + r.X * windowRect.Width;
            var absY = windowRect.Top + r.Y * windowRect.Height;
            var absW = r.Width * windowRect.Width;
            var absH = r.Height * windowRect.Height;
            var normalized = new NormalizedRegion(
                (absX - virtualScreen.Left) / Math.Max(1, virtualScreen.Width),
                (absY - virtualScreen.Top) / Math.Max(1, virtualScreen.Height),
                absW / Math.Max(1, virtualScreen.Width),
                absH / Math.Max(1, virtualScreen.Height));
            return (true, local.Confidence, (NormalizedRegion?)normalized);
        }, cancellationToken);
    }

    public Task<(bool Found, double Confidence, NormalizedRegion? Location)> FindTemplateAsync(
        byte[] frame, byte[] templateImageBytes, double confidenceThreshold, CancellationToken cancellationToken = default)
    {
        if (frame is null || frame.Length == 0) throw new ArgumentException("Кадр экрана пуст.", nameof(frame));
        if (templateImageBytes is null || templateImageBytes.Length == 0) throw new ArgumentException("Шаблон пуст.", nameof(templateImageBytes));
        if (confidenceThreshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidenceThreshold));

        return Task.Run(() => MatchTemplate(frame, templateImageBytes, confidenceThreshold, cancellationToken), cancellationToken);
    }

    public Task<(bool Found, double Confidence, NormalizedRegion? Location)> FindTemplateAsync(
        byte[] frame, string templateImagePath, double confidenceThreshold, CancellationToken cancellationToken = default)
    {
        if (frame is null || frame.Length == 0) throw new ArgumentException("Кадр экрана пуст.", nameof(frame));
        if (!File.Exists(templateImagePath)) throw new FileNotFoundException("Файл шаблона не найден.", templateImagePath);
        if (confidenceThreshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidenceThreshold));
        var bytes = File.ReadAllBytes(templateImagePath);
        return Task.Run(() => MatchTemplate(frame, bytes, confidenceThreshold, cancellationToken), cancellationToken);
    }

    private static (bool Found, double Confidence, NormalizedRegion? Location) MatchTemplate(
        byte[] frame, byte[] templateBytes, double confidenceThreshold, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = Cv2.ImDecode(frame, ImreadModes.Color);
        using var needle = Cv2.ImDecode(templateBytes, ImreadModes.Color);
        if (source.Empty()) throw new ArgumentException("Кадр экрана — не валидное изображение.", nameof(frame));
        if (needle.Empty()) throw new InvalidDataException("Шаблон не является валидным изображением.");
        if (needle.Width > source.Width || needle.Height > source.Height)
            return (false, 0.0, null);
        using var result = new Mat();
        Cv2.MatchTemplate(source, needle, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out double maxValue, out _, out var maxLocation);
        cancellationToken.ThrowIfCancellationRequested();
        double confidence = Math.Clamp(maxValue, 0, 1);
        if (confidence < confidenceThreshold) return (false, confidence, null);
        var region = new NormalizedRegion(
            (double)maxLocation.X / source.Width,
            (double)maxLocation.Y / source.Height,
            (double)needle.Width / source.Width,
            (double)needle.Height / source.Height);
        return (true, confidence, region);
    }

    public Task<(bool Found, double Confidence, NormalizedRegion? Location)> FindTemplateInWindowAsync(
        byte[] frame, byte[] templateImageBytes, double confidenceThreshold,
        (int Left, int Top, int Width, int Height) windowRect, VirtualScreenBounds virtualScreen,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var local = await FindTemplateAsync(frame, templateImageBytes, confidenceThreshold, cancellationToken);
            if (!local.Found || local.Location is null) return local;
            var r = local.Location.Value;
            var absX = windowRect.Left + r.X * windowRect.Width;
            var absY = windowRect.Top + r.Y * windowRect.Height;
            var absW = r.Width * windowRect.Width;
            var absH = r.Height * windowRect.Height;
            var normalized = new NormalizedRegion(
                (absX - virtualScreen.Left) / Math.Max(1, virtualScreen.Width),
                (absY - virtualScreen.Top) / Math.Max(1, virtualScreen.Height),
                absW / Math.Max(1, virtualScreen.Width),
                absH / Math.Max(1, virtualScreen.Height));
            return (true, local.Confidence, (NormalizedRegion?)normalized);
        }, cancellationToken);
    }

    public Task<bool> CheckPixelAsync(byte[] frame, double normalizedX, double normalizedY,
        (byte R, byte G, byte B) expectedColor, int tolerance, CancellationToken cancellationToken = default)
    {
        _ = frame;

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            int virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int screenWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            var (x, y) = ScreenCoordinateMath.ToAbsolutePixels(normalizedX, normalizedY, screenWidth, screenHeight);
            x += virtualLeft;
            y += virtualTop;

            nint hdc = GetDC(0);
            if (hdc == 0) throw new InvalidOperationException($"GetDC не сработал (Win32 error {Marshal.GetLastWin32Error()}).");
            try
            {
                uint color = GetPixel(hdc, x, y);
                if (color == INVALID_COLOR)
                    throw new InvalidOperationException($"GetPixel не сработал в точке ({x},{y}) (Win32 error {Marshal.GetLastWin32Error()}).");

                byte actualR = (byte)(color & 0xFF);
                byte actualG = (byte)((color >> 8) & 0xFF);
                byte actualB = (byte)((color >> 16) & 0xFF);

                return Math.Abs(actualR - expectedColor.R) <= tolerance
                    && Math.Abs(actualG - expectedColor.G) <= tolerance
                    && Math.Abs(actualB - expectedColor.B) <= tolerance;
            }
            finally { ReleaseDC(0, hdc); }
        }, cancellationToken);
    }

    public Task<string> ReadTextAsync(byte[] frame, NormalizedRegion region, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(TessdataPath))
            throw new InvalidOperationException($"{nameof(TessdataPath)} не задан — укажите каталог с *.traineddata перед вызовом OCR.");

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var full = Cv2.ImDecode(frame, ImreadModes.Color);
            if (full.Empty()) throw new ArgumentException("Кадр экрана — не валидное изображение.", nameof(frame));

            var crop = new OpenCvSharp.Rect(
                (int)(region.X * full.Width),
                (int)(region.Y * full.Height),
                Math.Max(1, (int)(region.Width * full.Width)),
                Math.Max(1, (int)(region.Height * full.Height)));
            crop = crop.Intersect(new OpenCvSharp.Rect(0, 0, full.Width, full.Height));

            using var cropped = new Mat(full, crop);
            Cv2.ImEncode(".png", cropped, out var croppedPng);

            lock (_ocrGate)
            {
                var engine = GetOrCreateOcrEngine();
                using var pix = Pix.LoadFromMemory(croppedPng);
                using var page = engine.Process(pix, PageSegMode.Auto);
                return page.GetText().Trim();
            }
        }, cancellationToken);
    }

    private TesseractEngine GetOrCreateOcrEngine()
    {
        if (_ocrEngine is not null && _ocrEngineLanguage == OcrLanguage) return _ocrEngine;

        _ocrEngine?.Dispose();
        if (!Directory.Exists(TessdataPath))
            throw new DirectoryNotFoundException($"Каталог tessdata не найден: {TessdataPath}");

        _ocrEngine = new TesseractEngine(TessdataPath, OcrLanguage, EngineMode.Default);
        _ocrEngineLanguage = OcrLanguage;
        return _ocrEngine;
    }

    private byte[]? _lastFrameHash;
    private DateTime _lastFrameChangedAtUtc = DateTime.UtcNow;

    public bool DetectWindowHang(byte[] currentFrame, TimeSpan hangThreshold)
    {
        ArgumentNullException.ThrowIfNull(currentFrame);
        var currentHash = System.Security.Cryptography.SHA256.HashData(currentFrame);
        bool changed = _lastFrameHash is null || !currentHash.AsSpan().SequenceEqual(_lastFrameHash);

        if (changed)
        {
            _lastFrameHash = currentHash;
            _lastFrameChangedAtUtc = DateTime.UtcNow;
            return false;
        }

        return DateTime.UtcNow - _lastFrameChangedAtUtc >= hangThreshold;
    }

    public void Dispose() => _ocrEngine?.Dispose();
}
