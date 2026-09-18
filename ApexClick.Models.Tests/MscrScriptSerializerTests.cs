using ApexClick.Models;

namespace ApexClick.Models.Tests;

public class MscrScriptSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesEventsAndOrder()
    {
        var script = new MacroScript { Name = "Test", PlaybackSpeedMultiplier = 1.5 };
        script.Events.Add(new MacroEvent { TimestampMs = 0, Type = MacroEventType.MouseMove, NormalizedX = 0.25, NormalizedY = 0.75 });
        script.Events.Add(new MacroEvent { TimestampMs = 50, Type = MacroEventType.KeyDown, VirtualKeyCode = 0x41, ScanCode = 30, KeyboardLayoutId = "ru-RU" });

        var loaded = MscrScriptSerializer.FromJson(MscrScriptSerializer.ToJson(script));

        Assert.Equal(script.Name, loaded.Name);
        Assert.Equal(script.PlaybackSpeedMultiplier, loaded.PlaybackSpeedMultiplier);
        Assert.Equal(2, loaded.Events.Count);
        Assert.Equal(MacroEventType.MouseMove, loaded.Events[0].Type);
        Assert.Equal(0.25, loaded.Events[0].NormalizedX);
        Assert.Equal(MacroEventType.KeyDown, loaded.Events[1].Type);
        Assert.Equal("ru-RU", loaded.Events[1].KeyboardLayoutId);
    }

    [Fact]
    public void RoundTrip_WheelPayload_SurvivesViaPayloadConvert()
    {
        
        var script = new MacroScript();
        script.Events.Add(new MacroEvent { Type = MacroEventType.MouseWheel, Payload = -120 });

        var loaded = MscrScriptSerializer.FromJson(MscrScriptSerializer.ToJson(script));

        Assert.Equal(-120, PayloadConvert.TryGetInt32(loaded.Events[0].Payload));
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsThroughDisk()
    {
        var script = new MacroScript { Name = "Disk round-trip" };
        script.Events.Add(new MacroEvent { Type = MacroEventType.MouseButtonDown, VirtualKeyCode = MouseVirtualKeys.Left });

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mscr");
        try
        {
            await MscrScriptSerializer.SaveAsync(script, path);
            var loaded = await MscrScriptSerializer.LoadAsync(path);

            Assert.Equal("Disk round-trip", loaded.Name);
            Assert.NotNull(loaded.LastModifiedUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromJson_RejectsNewerFormatVersion()
    {
        var json = """{"FormatVersion":999,"Script":{"Id":"11111111-1111-1111-1111-111111111111","Name":"x"}}""";
        Assert.Throws<NotSupportedException>(() => MscrScriptSerializer.FromJson(json));
    }
}
