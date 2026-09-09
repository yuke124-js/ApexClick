namespace ApexClick.Models;

public sealed class MacroScript
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Untitled Script";

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime? LastModifiedUtc { get; set; }

    public double PlaybackSpeedMultiplier { get; set; } = 1.0;

    public List<MacroEvent> Events { get; init; } = new();

    public Dictionary<string, string> Metadata { get; init; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, byte[]> EmbeddedAssets { get; init; } = new();

    public string? CommunityCategory { get; set; }

    public List<ScreenTrigger> Triggers { get; init; } = new();

}
