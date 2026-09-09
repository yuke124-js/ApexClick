using ApexClick.Models;

namespace ApexClick.Models.Tests;

public class MacroScriptTests
{
    [Fact]
    public void NewScript_HasSensibleDefaults()
    {
        var script = new MacroScript();

        Assert.NotEqual(Guid.Empty, script.Id);
        Assert.Equal("Untitled Script", script.Name);
        Assert.Empty(script.Events);
        Assert.Empty(script.Triggers);
        Assert.Equal(1.0, script.PlaybackSpeedMultiplier);
        Assert.Null(script.CommunityCategory);
    }

    [Fact]
    public void Events_PreserveInsertionOrder()
    {
        
        var script = new MacroScript();
        var first = new MacroEvent { TimestampMs = 0, Type = MacroEventType.MouseMove };
        var second = new MacroEvent { TimestampMs = 100, Type = MacroEventType.MouseButtonDown };

        script.Events.Add(first);
        script.Events.Add(second);

        Assert.Equal(new[] { first.Id, second.Id }, script.Events.Select(e => e.Id));
    }
}

public class ScreenTriggerTests
{
    [Fact]
    public void NewTrigger_HasDefaultConfidenceThreshold()
    {
        var trigger = new ScreenTrigger();

        Assert.Equal(0.9, trigger.ConfidenceThreshold);
        Assert.Null(trigger.SearchRegion);
        Assert.Empty(trigger.RecoveryChain);
    }
}

public class NormalizedRegionTests
{
    [Fact]
    public void Equality_IsValueBased()
    {
        
        var a = new NormalizedRegion(0.1, 0.2, 0.3, 0.4);
        var b = new NormalizedRegion(0.1, 0.2, 0.3, 0.4);

        Assert.Equal(a, b);
    }
}
