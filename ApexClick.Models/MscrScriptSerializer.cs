using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApexClick.Models;

public static class MscrScriptSerializer
{
    private const int LegacyJsonVersion = 1;
    private const int ContainerFormatVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(MacroScript script) =>
        JsonSerializer.Serialize(
            new LegacyEnvelope
            {
                FormatVersion = LegacyJsonVersion,
                Script = script
            },
            Options);

    public static MacroScript FromJson(string json)
    {
        var envelope = JsonSerializer.Deserialize<LegacyEnvelope>(json, Options)
            ?? throw new InvalidDataException("Файл .mscr пуст или повреждён.");

        if (envelope.Script is null)
            throw new InvalidDataException("Файл .mscr не содержит сценария.");

        if (envelope.FormatVersion > LegacyJsonVersion)
            throw new NotSupportedException(
                $"Файл сохранён более новой версией формата ({envelope.FormatVersion}), " +
                $"поддерживается до {LegacyJsonVersion}.");

        return envelope.Script;
    }

    public static async Task SaveAsync(
        MacroScript script,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        script.LastModifiedUtc = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

        string fullPath = Path.GetFullPath(filePath);
        if (!fullPath.EndsWith(".mscr", StringComparison.OrdinalIgnoreCase))
            fullPath += ".mscr";

        var graph = new MacroGraphOptimizer().Build(script);
        ValidateBeforeSave(script);

        await using var fs = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);

        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true);

        var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using (var stream = manifest.Open())
        {
            var model = MscrManifest.From(script, graph);
            await JsonSerializer.SerializeAsync(stream, model, Options, cancellationToken);
        }

        if (script.Events.Count > 0)
        {
            var eventsEntry = archive.CreateEntry("events.bin", CompressionLevel.Optimal);
            await using var eventsStream = eventsEntry.Open();
            MscrEventStreamCodec.Write(eventsStream, script.Events);
        }

        foreach (var asset in script.EmbeddedAssets)
        {
            string assetId = NormalizeAssetId(asset.Key);
            var entry = archive.CreateEntry("assets/" + assetId, CompressionLevel.Optimal);
            await using var assetStream = entry.Open();
            await assetStream.WriteAsync(asset.Value.AsMemory(), cancellationToken);
        }
    }

    public static async Task<MacroScript> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var fs = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);

        if (!LooksLikeZip(fs))
        {
            fs.Position = 0;
            using var reader = new StreamReader(fs);
            string json = await reader.ReadToEndAsync(cancellationToken);
            return FromJson(json);
        }

        using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Файл .mscr не содержит manifest.json.");

        MscrManifest manifest;
        await using (var stream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<MscrManifest>(
                stream, Options, cancellationToken)
                ?? throw new InvalidDataException("manifest.json повреждён.");
        }

        if (manifest.FormatVersion > ContainerFormatVersion)
            throw new NotSupportedException(
                $"Файл сохранён более новой версией .mscr ({manifest.FormatVersion}), " +
                $"поддерживается до {ContainerFormatVersion}.");

        var script = manifest.ToScript();

        var eventsEntry = archive.GetEntry("events.bin");
        if (eventsEntry is not null)
        {
            await using var stream = eventsEntry.Open();
            script.Events.AddRange(MscrEventStreamCodec.Read(stream));
        }

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("assets/", StringComparison.Ordinal) ||
                entry.FullName.EndsWith('/'))
                continue;

            string assetId = entry.FullName["assets/".Length..];
            await using var input = entry.Open();
            using var memory = new MemoryStream();
            await input.CopyToAsync(memory, cancellationToken);
            script.EmbeddedAssets[assetId] = memory.ToArray();
        }

        ValidateBeforeSave(script);
        return script;
    }

    private static void ValidateBeforeSave(MacroScript script)
    {
        if (script.PlaybackSpeedMultiplier <= 0)
            throw new InvalidDataException("PlaybackSpeedMultiplier must be greater than zero.");

        long previous = -1;
        foreach (var evt in script.Events)
        {
            if (evt.TimestampMs < previous)
                throw new InvalidDataException("MacroEvent timestamps must be monotonic.");
            previous = evt.TimestampMs;
        }

        foreach (var trigger in script.Triggers)
        {
            if (trigger.ConfidenceThreshold is < 0 or > 1)
                throw new InvalidDataException(
                    $"ScreenTrigger '{trigger.Id}' has ConfidenceThreshold outside 0..1.");
        }
    }

    private static bool LooksLikeZip(Stream stream)
    {
        long originalPosition = stream.Position;
        Span<byte> signature = stackalloc byte[4];
        int read = stream.Read(signature);
        stream.Position = originalPosition;

        return read == 4 &&
               signature[0] == (byte)'P' &&
               signature[1] == (byte)'K' &&
               signature[2] == 3 &&
               signature[3] == 4;
    }

    private static string NormalizeAssetId(string id)
    {
        string normalized = id.Replace('\\', '/').Trim('/');

        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Некорректный asset id.");

        return normalized;
    }

    private sealed class LegacyEnvelope
    {
        public int FormatVersion { get; init; }
        public MacroScript? Script { get; init; }
    }

    private sealed class MscrManifest
    {
        public int FormatVersion { get; init; }
        public ScriptData Script { get; init; } = new();
        public GraphData Graph { get; init; } = new();

        public static MscrManifest From(MacroScript script, MacroGraph graph) =>
            new()
            {
                FormatVersion = ContainerFormatVersion,
                Script = ScriptData.From(script),
                Graph = GraphData.From(graph)
            };

        public MacroScript ToScript() => Script.ToScript();

        public sealed class ScriptData
        {
            public Guid Id { get; init; }
            public string Name { get; init; } = "Untitled Script";
            public DateTime CreatedAtUtc { get; init; }
            public DateTime? LastModifiedUtc { get; init; }
            public double PlaybackSpeedMultiplier { get; init; }
            public string? CommunityCategory { get; init; }
            public List<ScreenTrigger> Triggers { get; init; } = new();
            public Dictionary<string, string> Metadata { get; init; } = new();

            public static ScriptData From(MacroScript script) => new()
            {
                Id = script.Id,
                Name = script.Name,
                CreatedAtUtc = script.CreatedAtUtc,
                LastModifiedUtc = script.LastModifiedUtc,
                PlaybackSpeedMultiplier = script.PlaybackSpeedMultiplier,
                CommunityCategory = script.CommunityCategory,
                Triggers = script.Triggers.ToList(),
                Metadata = new Dictionary<string, string>(script.Metadata)
            };

            public MacroScript ToScript()
            {
                var script = new MacroScript
                {
                    Id = Id,
                    Name = Name,
                    CreatedAtUtc = CreatedAtUtc,
                    LastModifiedUtc = LastModifiedUtc,
                    PlaybackSpeedMultiplier = PlaybackSpeedMultiplier,
                    CommunityCategory = CommunityCategory
                };

                foreach (var trigger in Triggers)
                    script.Triggers.Add(trigger);

                foreach (var item in Metadata)
                    script.Metadata[item.Key] = item.Value;

                return script;
            }
        }

        public sealed class GraphData
        {
            public List<GraphNodeData> Nodes { get; init; } = new();
            public List<GraphEdgeData> Edges { get; init; } = new();

            public static GraphData From(MacroGraph graph) => new()
            {
                Nodes = graph.Nodes.Select(n => new GraphNodeData
                {
                    Id = n.Id,
                    DisplayType = n.DisplayType,
                    SourceEventIds = n.SourceEvents.Select(e => e.Id).ToList(),
                    PathMode = n.PathMode,
                    PathPoints = n.PathPoints?.ToList()
                }).ToList(),
                Edges = graph.Edges.Select(e => new GraphEdgeData
                {
                    Id = e.Id,
                    SourceNodeId = e.SourceNodeId,
                    TargetNodeId = e.TargetNodeId
                }).ToList()
            };
        }

        public sealed class GraphNodeData
        {
            public string Id { get; init; } = string.Empty;
            public string DisplayType { get; init; } = string.Empty;
            public List<Guid> SourceEventIds { get; init; } = new();
            public MousePathMode? PathMode { get; init; }
            public List<MousePathPoint>? PathPoints { get; init; }
        }

        public sealed class GraphEdgeData
        {
            public string Id { get; init; } = string.Empty;
            public string SourceNodeId { get; init; } = string.Empty;
            public string TargetNodeId { get; init; } = string.Empty;
        }
    }
}
