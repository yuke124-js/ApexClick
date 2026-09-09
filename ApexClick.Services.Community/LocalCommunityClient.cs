using System.Text.Json;
using ApexClick.Models;

namespace ApexClick.Services.Community;

public sealed class LocalCommunityClient : ICommunityClient
{
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ApexClick", "Community");

    public LocalCommunityClient() => Directory.CreateDirectory(_root);

    public async Task<CommunityScriptSummary> PublishAsync(
        MacroScript script, string category, string author,
        CancellationToken cancellationToken = default)
    {
        CommunitySafety.ValidateForPublication(script, category);
        var dir = Path.Combine(_root, script.Id.ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "script.mscr");
        await MscrScriptSerializer.SaveAsync(script, file, cancellationToken);
        var summary = new CommunityScriptSummary(
            script.Id, script.Name, category, 0, author);
        await File.WriteAllTextAsync(
            Path.Combine(dir, "metadata.json"),
            System.Text.Json.JsonSerializer.Serialize(summary),
            cancellationToken);
        return summary;
    }

    public async Task<IReadOnlyList<CommunityScriptSummary>> SearchAsync(
        string? category, string? query,
        CancellationToken cancellationToken = default)
    {
        var result = new List<CommunityScriptSummary>();
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(dir, "metadata.json");
            if (!File.Exists(metadataPath)) continue;
            try
            {
                var summary = System.Text.Json.JsonSerializer.Deserialize<CommunityScriptSummary>(
                    await File.ReadAllTextAsync(metadataPath, cancellationToken));
                if (summary is null) continue;
                if (!string.IsNullOrWhiteSpace(category) &&
                    !string.Equals(summary.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(query) &&
                    !summary.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(summary);
            }
            catch (JsonException) { }
        }
        return result;
    }

    public Task<MacroScript> DownloadAsync(Guid scriptId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, scriptId.ToString("N"), "script.mscr");
        if (!File.Exists(path))
            throw new FileNotFoundException("Сценарий не найден в локальной галерее.", path);
        return MscrScriptSerializer.LoadAsync(path, cancellationToken);
    }
}
