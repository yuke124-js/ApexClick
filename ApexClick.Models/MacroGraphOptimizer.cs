namespace ApexClick.Models;

public sealed class MacroGraphOptimizer
{
    public MousePathOptimizerOptions Options { get; set; } = new();

    public MacroGraph Build(MacroScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var width = GetPositiveMetadata(script, "recording.virtualWidth")
            ?? GetPositiveMetadata(script, "recording.screenWidth")
            ?? Options.DefaultScreenWidth;
        var height = GetPositiveMetadata(script, "recording.virtualHeight")
            ?? GetPositiveMetadata(script, "recording.screenHeight")
            ?? Options.DefaultScreenHeight;

        return Build(script.Events, width, height);
    }

    public MacroGraph Build(
        IReadOnlyList<MacroEvent> events,
        int screenWidth,
        int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(events);
        screenWidth = Math.Max(1, screenWidth);
        screenHeight = Math.Max(1, screenHeight);

        var ordered = events.ToArray();
        var nodes = new List<MacroGraphNode>();
        var index = 0;

        for (var i = 0; i < ordered.Length;)
        {
            if (ordered[i].Type == MacroEventType.MouseMove)
            {
                var (rawSegment, nextIndex) = ReadMotionSegment(ordered, i, screenWidth, screenHeight);

                if (rawSegment.Count >= Options.MinimumMoveSamples)
                {
                    var normalizedTolerance = Math.Max(
                        Options.CollinearityTolerancePixels / screenWidth,
                        Options.CollinearityTolerancePixels / screenHeight);

                    var simplified = Options.SimplifyPaths
                        ? MousePathSimplifier.Simplify(rawSegment, normalizedTolerance, Options.MaxPathPoints)
                        : rawSegment.Count <= Options.MaxPathPoints
                            ? rawSegment.ToArray()
                            : rawSegment.Take(Options.MaxPathPoints - 1).Append(rawSegment[^1]).ToArray();

                    if (simplified.Count >= 2 &&
                        DistanceInPixels(
                            simplified[0],
                            simplified[^1],
                            screenWidth,
                            screenHeight) >= Options.MinimumSpatialDistancePixels)
                    {
                        nodes.Add(new MacroGraphNode
                        {
                            Id = $"n{index++}",
                            DisplayType = "MousePath",
                            RuntimeType = MacroEventType.MouseMove,
                            SourceEvents = ordered[i..nextIndex].ToArray(),
                            PathMode = DetermineMode(simplified),
                            PathPoints = simplified,
                            EditorX = 40,
                            EditorY = nodes.Count * 110 + 40
                        });

                        i = nextIndex;
                        continue;
                    }
                }
            }

            nodes.Add(new MacroGraphNode
            {
                Id = $"n{index++}",
                DisplayType = ordered[i].Type.ToString(),
                RuntimeType = ordered[i].Type,
                SourceEvents = new[] { ordered[i] },
                EditorX = 40,
                EditorY = nodes.Count * 90 + 40
            });
            i++;
        }

        var edges = new List<MacroGraphEdge>(Math.Max(0, nodes.Count - 1));
        for (var i = 1; i < nodes.Count; i++)
        {
            edges.Add(new MacroGraphEdge
            {
                Id = $"e{i - 1}",
                SourceNodeId = nodes[i - 1].Id,
                TargetNodeId = nodes[i].Id
            });
        }

        return new MacroGraph
        {
            Nodes = nodes,
            Edges = edges
        };
    }

    private (List<MousePathPoint> Segment, int NextIndex) ReadMotionSegment(
        IReadOnlyList<MacroEvent> events,
        int start,
        int screenWidth,
        int screenHeight)
    {
        var segment = new List<MousePathPoint>();
        var i = start;
        long segmentStart = events[start].TimestampMs;
        long lastTimestamp = segmentStart;
        double? lastX = null;
        double? lastY = null;

        while (i < events.Count && events[i].Type == MacroEventType.MouseMove)
        {
            var e = events[i];

            if (e.NormalizedX is not { } x || e.NormalizedY is not { } y)
                break;

            if (segment.Count > 0)
            {
                var gap = e.TimestampMs - lastTimestamp;
                if (gap > Options.MaxGapMs)
                    break;

                var jitter = Math.Abs(x - lastX!.Value) + Math.Abs(y - lastY!.Value);
                var jitterThreshold = Math.Min(
                    Options.MinimumSpatialDistancePixels / 4d / screenWidth,
                    Options.MinimumSpatialDistancePixels / 4d / screenHeight);

                if (jitter < jitterThreshold)
                {
                    i++;
                    lastTimestamp = e.TimestampMs;
                    continue;
                }
            }

            segment.Add(new MousePathPoint
            {
                OffsetMs = e.TimestampMs - segmentStart,
                X = x,
                Y = y
            });

            lastX = x;
            lastY = y;
            lastTimestamp = e.TimestampMs;
            i++;
        }

        return (segment, i);
    }

    private static MousePathMode DetermineMode(IReadOnlyList<MousePathPoint> points)
    {
        if (points.Count <= 2)
            return MousePathMode.Linear;

        var first = points[0];
        var last = points[^1];
        var dx = Math.Abs(last.X - first.X);
        var dy = Math.Abs(last.Y - first.Y);

        if ((dx > 0.05 && dy < 0.02) || (dy > 0.05 && dx < 0.02))
            return MousePathMode.Linear;

        var changes = 0;
        var previousX = 0;
        var previousY = 0;

        for (var i = 1; i < points.Count; i++)
        {
            var sx = Math.Sign(points[i].X - points[i - 1].X);
            var sy = Math.Sign(points[i].Y - points[i - 1].Y);

            if (sx != 0 && previousX != 0 && sx != previousX) changes++;
            if (sy != 0 && previousY != 0 && sy != previousY) changes++;

            if (sx != 0) previousX = sx;
            if (sy != 0) previousY = sy;
        }

        return changes > 1 ? MousePathMode.Smooth : MousePathMode.Polyline;
    }

    private static double DistanceInPixels(
        MousePathPoint a,
        MousePathPoint b,
        int width,
        int height)
    {
        var dx = (b.X - a.X) * width;
        var dy = (b.Y - a.Y) * height;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int? GetPositiveMetadata(MacroScript script, string key)
    {
        return script.Metadata.TryGetValue(key, out var value) &&
               int.TryParse(value, out var parsed) &&
               parsed > 0
            ? parsed
            : null;
    }
}
