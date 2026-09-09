namespace ApexClick.Models;

public sealed class ScreenTrigger
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string? TemplateImagePath { get; init; }

    public double ConfidenceThreshold { get; init; } = 0.9;

    public NormalizedRegion? SearchRegion { get; init; }

    public List<MacroEvent> RecoveryChain { get; init; } = new();

    public Guid ResumeAfterEventId { get; init; }

    public bool IsPixelTrigger { get; init; }
    public double PixelNormalizedX { get; init; }
    public double PixelNormalizedY { get; init; }
    public byte PixelR { get; init; }
    public byte PixelG { get; init; }
    public byte PixelB { get; init; }
    public byte PixelTolerance { get; init; } = 10;

}

public readonly record struct NormalizedRegion(double X, double Y, double Width, double Height);
