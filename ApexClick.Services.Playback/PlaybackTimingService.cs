using ApexClick.Models;

namespace ApexClick.Services.Playback;

public sealed class PlaybackTimingService
{
    
    public int DefaultInterActionDelayMs { get; set; } = 8;

    public int MaxRecordedGapMs { get; set; } = 400;

    public TimeSpan GetInterActionDelay(MacroScript script) => GetInterActionDelay(script, null);

    public TimeSpan GetInterActionDelay(MacroScript script, long? recordedGapMs)
    {
        double multiplier = script.PlaybackSpeedMultiplier > 0 ? script.PlaybackSpeedMultiplier : 1.0;
        double baseMs = recordedGapMs is { } gap ? Math.Min(gap, MaxRecordedGapMs) : DefaultInterActionDelayMs;
        double ms = Math.Max(baseMs / multiplier, 1.0);
        return TimeSpan.FromMilliseconds(ms);
    }
}
