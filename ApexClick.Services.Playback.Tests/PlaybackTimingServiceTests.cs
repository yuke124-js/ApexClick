using ApexClick.Models;
using ApexClick.Services.Playback;

namespace ApexClick.Services.Playback.Tests;

public class PlaybackTimingServiceTests
{
    [Fact]
    public void DefaultSpeed_UsesDefaultInterActionDelay()
    {
        var timing = new PlaybackTimingService();
        var script = new MacroScript { PlaybackSpeedMultiplier = 1.0 };

        var delay = timing.GetInterActionDelay(script);

        Assert.Equal(TimeSpan.FromMilliseconds(timing.DefaultInterActionDelayMs), delay);
    }

    [Fact]
    public void DoubleSpeed_HalvesTheDelay()
    {
        var timing = new PlaybackTimingService();
        var script = new MacroScript { PlaybackSpeedMultiplier = 2.0 };

        var delay = timing.GetInterActionDelay(script);

        Assert.Equal(TimeSpan.FromMilliseconds(timing.DefaultInterActionDelayMs / 2.0), delay);
    }

    [Fact]
    public void Delay_NeverGoesBelowOneMillisecond()
    {
        var timing = new PlaybackTimingService { DefaultInterActionDelayMs = 1 };
        var script = new MacroScript { PlaybackSpeedMultiplier = 2.0 };

        var delay = timing.GetInterActionDelay(script);

        Assert.True(delay >= TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void RecordedGap_IsUsedInsteadOfDefault()
    {
        var timing = new PlaybackTimingService();
        var script = new MacroScript { PlaybackSpeedMultiplier = 1.0 };

        var delay = timing.GetInterActionDelay(script, recordedGapMs: 50);

        Assert.Equal(TimeSpan.FromMilliseconds(50), delay);
    }

    [Fact]
    public void LongRecordedGap_IsCappedAtMaxRecordedGapMs()
    {
        var timing = new PlaybackTimingService { MaxRecordedGapMs = 400 };
        var script = new MacroScript { PlaybackSpeedMultiplier = 1.0 };

        var delay = timing.GetInterActionDelay(script, recordedGapMs: 30000);

        Assert.Equal(TimeSpan.FromMilliseconds(400), delay);
    }
}
