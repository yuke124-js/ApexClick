using ApexClick.Models;
using ApexClick.Services.DataDriven;
using ApexClick.FeaturePack.GraphEngine;

namespace ApexClick.FeaturePack.DataDriven;

public sealed class DataDrivenRunner
{
    public MacroScript ApplyRow(MacroScript source, IReadOnlyDictionary<string,string> row)
    {
        var result = new MacroScript {
            Id = source.Id,
            Name = source.Name,
            CreatedAtUtc = source.CreatedAtUtc,
            LastModifiedUtc = DateTime.UtcNow,
            PlaybackSpeedMultiplier = source.PlaybackSpeedMultiplier,
            CommunityCategory = source.CommunityCategory
        };
        foreach (var e in source.Events)
            result.Events.Add(new MacroEvent {
                Id=e.Id, TimestampMs=e.TimestampMs, Type=e.Type,
                NormalizedX=e.NormalizedX, NormalizedY=e.NormalizedY,
                VirtualKeyCode=e.VirtualKeyCode, ScanCode=e.ScanCode,
                KeyboardLayoutId=e.KeyboardLayoutId,
                Payload=PlaceholderEngine.ReplacePayload(e.Payload,row)
            });
        foreach (var m in source.Metadata)
            result.Metadata[m.Key]=PlaceholderEngine.Replace(m.Value,row);
        result.Triggers.AddRange(source.Triggers);
        if (source.Metadata.TryGetValue("execution.graph", out var graphJson)) result.Metadata["execution.graph"] = PlaceholderEngine.Replace(graphJson, row);
        return result;
    }

    public async IAsyncEnumerable<MacroScript> RunCsvAsync(
        MacroScript source, string csvPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var ds = new CsvDataSource(csvPath);
        await foreach (var row in ds.ReadRowsAsync(ct))
            yield return ApplyRow(source, row);
    }

    public async Task<IReadOnlyList<MacroScript>> ExpandCsvAsync(MacroScript source, string csvPath, CancellationToken ct=default)
    {
        var ds = new CsvDataSource(csvPath);
        var list = new List<MacroScript>();
        await foreach (var row in ds.ReadRowsAsync(ct))
            list.Add(ApplyRow(source,row));
        return list;
    }
}
