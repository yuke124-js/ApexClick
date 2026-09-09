using ApexClick.Models;

namespace ApexClick.Models.Tests;

public sealed class MscrContainerOptimizationTests
{
    [Fact]
    public async Task HybridContainer_RoundTripsExistingMacroEventContract()
    {
        var script = new MacroScript
        {
            Name = "Hybrid",
            PlaybackSpeedMultiplier = 1.5,
            CommunityCategory = "productivity"
        };

        script.Metadata["recording.screenWidth"] = "1920";
        script.Metadata["recording.screenHeight"] = "1080";

        script.Events.Add(new MacroEvent
        {
            TimestampMs = 0,
            Type = MacroEventType.MouseMove,
            NormalizedX = 0.1,
            NormalizedY = 0.2
        });

        script.Events.Add(new MacroEvent
        {
            TimestampMs = 12,
            Type = MacroEventType.MouseButtonDown,
            VirtualKeyCode = MouseVirtualKeys.Left,
            NormalizedX = 0.3,
            NormalizedY = 0.2
        });

        script.Events.Add(new MacroEvent
        {
            TimestampMs = 25,
            Type = MacroEventType.KeyDown,
            VirtualKeyCode = 0x41,
            ScanCode = 30,
            KeyboardLayoutId = "ru-RU"
        });

        script.Events.Add(new MacroEvent
        {
            TimestampMs = 26,
            Type = MacroEventType.MouseWheel,
            NormalizedX = 0.3,
            NormalizedY = 0.2,
            Payload = -120
        });

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mscr");

        try
        {
            await MscrScriptSerializer.SaveAsync(script, path);
            var loaded = await MscrScriptSerializer.LoadAsync(path);

            Assert.Equal(script.Id, loaded.Id);
            Assert.Equal(script.PlaybackSpeedMultiplier, loaded.PlaybackSpeedMultiplier);
            Assert.Equal(script.CommunityCategory, loaded.CommunityCategory);
            Assert.Equal(script.Events.Count, loaded.Events.Count);
            Assert.Equal(script.Events[1].VirtualKeyCode, loaded.Events[1].VirtualKeyCode);
            Assert.Equal("ru-RU", loaded.Events[2].KeyboardLayoutId);
            Assert.Equal(-120, PayloadConvert.TryGetInt32(loaded.Events[3].Payload));
            Assert.Equal("1920", loaded.Metadata["recording.screenWidth"]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ThousandMouseMovesBecomeOneGraphNode_WithoutChangingRawEvents()
    {
        var events = new List<MacroEvent>();
        for (int i = 0; i < 1000; i++)
        {
            double x = 0.1 + 0.8 * i / 999d;
            double y = 0.5 + (10d / 1080d) * i / 999d;

            events.Add(new MacroEvent
            {
                TimestampMs = i * 2,
                Type = MacroEventType.MouseMove,
                NormalizedX = x,
                NormalizedY = y
            });
        }

        var originalIds = events.Select(e => e.Id).ToArray();
        var script = new MacroScript();
        script.Metadata["recording.screenWidth"] = "1920";
        script.Metadata["recording.screenHeight"] = "1080";
        script.Events.AddRange(events);

        var graph = new MacroGraphOptimizer().Build(script);

        Assert.Single(graph.Nodes);
        Assert.Equal("MousePath", graph.Nodes[0].DisplayType);
        Assert.True(graph.Nodes[0].SourceEvents.Count > 900);
        Assert.Equal(originalIds, script.Events.Select(e => e.Id));
        Assert.True(graph.Nodes[0].PathPoints!.Count <= 32);
    }

    [Fact]
    public void ClickSplitsMousePathIntoSemanticGroups()
    {
        var script = new MacroScript();

        for (int i = 0; i < 20; i++)
        {
            script.Events.Add(new MacroEvent
            {
                TimestampMs = i * 5,
                Type = MacroEventType.MouseMove,
                NormalizedX = 0.1 + i / 100d,
                NormalizedY = 0.2
            });
        }

        script.Events.Add(new MacroEvent
        {
            TimestampMs = 105,
            Type = MacroEventType.MouseButtonDown,
            VirtualKeyCode = MouseVirtualKeys.Left,
            NormalizedX = 0.3,
            NormalizedY = 0.2
        });

        var graph = new MacroGraphOptimizer().Build(script);

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Equal("MousePath", graph.Nodes[0].DisplayType);
        Assert.Equal(nameof(MacroEventType.MouseButtonDown), graph.Nodes[1].DisplayType);
    }
    [Fact]
    public void EditorReorder_RebasesTimestampsInsteadOfDroppingTheEdit()
    {
        var script = new MacroScript();
        script.Events.Add(new MacroEvent { TimestampMs = 0, Type = MacroEventType.MouseMove, NormalizedX = 0.1, NormalizedY = 0.1 });
        script.Events.Add(new MacroEvent { TimestampMs = 100, Type = MacroEventType.MouseButtonDown, VirtualKeyCode = MouseVirtualKeys.Left, NormalizedX = 0.2, NormalizedY = 0.1 });

        var editor = new ApexClick.ViewModels.EditorViewModel();
        editor.LoadFromScript(script);

        editor.MoveDown(editor.Nodes[0]);
        var updated = editor.SaveToScript();

        Assert.Equal(2, updated.Events.Count);
        Assert.Equal(MacroEventType.MouseButtonDown, updated.Events[0].Type);
        Assert.Equal(MacroEventType.MouseMove, updated.Events[1].Type);
        Assert.True(updated.Events[0].TimestampMs <= updated.Events[1].TimestampMs);
    }

}
