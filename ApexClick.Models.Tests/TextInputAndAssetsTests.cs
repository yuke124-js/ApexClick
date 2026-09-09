using ApexClick.Models;

namespace ApexClick.Models.Tests;

public sealed class TextInputAndAssetsTests
{
    [Fact]
    public async Task MscrRoundTripPreservesTextInputAndEmbeddedImage()
    {
        var script = new MacroScript { Name = "editor" };
        script.Events.Add(new MacroEvent { Type = MacroEventType.TextInput, TimestampMs = 5, Payload = "Привет ApexClick" });
        script.EmbeddedAssets["template-a.png"] = [137, 80, 78, 71];

        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mscr");
        try
        {
            await MscrScriptSerializer.SaveAsync(script, file);
            var loaded = await MscrScriptSerializer.LoadAsync(file);
            Assert.Single(loaded.Events);
            Assert.Equal(MacroEventType.TextInput, loaded.Events[0].Type);
            Assert.Equal("Привет ApexClick", loaded.Events[0].Payload?.ToString());
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, loaded.EmbeddedAssets["template-a.png"]);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
