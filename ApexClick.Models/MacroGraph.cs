namespace ApexClick.Models;

public enum MousePathMode
{
    Linear,
    Polyline,
    Smooth
}

public sealed record MousePathPoint
{
    public long OffsetMs { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
}

public sealed class MacroGraphNode
{
    public required string Id { get; init; }
    public required string DisplayType { get; init; }
    public IReadOnlyList<MacroEvent> SourceEvents { get; init; } = Array.Empty<MacroEvent>();
    public MousePathMode? PathMode { get; init; }
    public IReadOnlyList<MousePathPoint>? PathPoints { get; init; }
    public MacroEventType RuntimeType { get; init; } = MacroEventType.Wait;
    public Dictionary<string,string> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public double EditorX { get; set; }
    public double EditorY { get; set; }

    public bool IsMousePath => PathMode is not null;
}

public enum MacroGraphEdgeKind { Next, True, False, Loop }

public sealed record MacroGraphEdge
{
    public required string Id { get; init; }
    public required string SourceNodeId { get; init; }
    public required string TargetNodeId { get; init; }
    public MacroGraphEdgeKind Kind { get; init; } = MacroGraphEdgeKind.Next;
}

public sealed class MacroGraph
{
    public IReadOnlyList<MacroGraphNode> Nodes { get; init; } = Array.Empty<MacroGraphNode>();
    public IReadOnlyList<MacroGraphEdge> Edges { get; init; } = Array.Empty<MacroGraphEdge>();
}

public sealed record MousePathOptimizerOptions
{
    public int MinimumMoveSamples { get; init; } = 4;
    public double CollinearityTolerancePixels { get; init; } = 10.0;
    public double MinimumSpatialDistancePixels { get; init; } = 2.0;
    public long MaxGapMs { get; init; } = 80;
    public int MaxPathPoints { get; init; } = 32;
    public bool SimplifyPaths { get; init; } = true;
    public int DefaultScreenWidth { get; init; } = 1920;
    public int DefaultScreenHeight { get; init; } = 1080;
}
