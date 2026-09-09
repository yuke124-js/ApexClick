namespace ApexClick.Models;

public sealed class MacroEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public long TimestampMs { get; init; }

    public MacroEventType Type { get; init; }

    public double? NormalizedX { get; init; }

    public double? NormalizedY { get; init; }

    public int? VirtualKeyCode { get; init; }

    public int? ScanCode { get; init; }

    public string? KeyboardLayoutId { get; init; }

    public object? Payload { get; init; }

}
