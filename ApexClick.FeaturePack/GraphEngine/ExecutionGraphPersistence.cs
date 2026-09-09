using System.Text.Json;
using ApexClick.Models;

namespace ApexClick.FeaturePack.GraphEngine;

public static class ExecutionGraphPersistence
{
    public const string MetadataKey = "execution.graph";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static void Save(MacroScript script, ExecutionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(graph);
        script.Metadata[MetadataKey] = JsonSerializer.Serialize(graph, Options);
    }

    public static bool TryLoad(MacroScript script, out ExecutionGraph? graph)
    {
        ArgumentNullException.ThrowIfNull(script);
        graph = null;

        if (!script.Metadata.TryGetValue(MetadataKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            graph = JsonSerializer.Deserialize<ExecutionGraph>(raw, Options);
            return graph is not null && graph.Nodes.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static ExecutionGraph FromMacroGraph(MacroGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var result = new ExecutionGraph();
        foreach (var node in graph.Nodes)
        {
            var executionNode = new ExecutionNode { Id=node.Id, Type=node.RuntimeType };
            foreach (var evt in node.SourceEvents) executionNode.EventIds.Add(evt.Id);
            foreach (var pair in node.Properties) executionNode.Properties[pair.Key]=pair.Value;

            var payload = node.SourceEvents.FirstOrDefault()?.Payload;
            if (payload is string s && executionNode.Type is MacroEventType.ConditionScreenTemplate or MacroEventType.ConditionScreenPixel or MacroEventType.ConditionOcrText or MacroEventType.Loop)
                executionNode.Properties["expression"] = s;
            else if (payload is JsonElement json && json.ValueKind == JsonValueKind.Object)
                foreach (var prop in json.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.String) executionNode.Properties[prop.Name]=prop.Value.GetString() ?? string.Empty;

            result.Nodes.Add(executionNode);
        }
        foreach (var edge in graph.Edges)
            result.Edges.Add(new ExecutionEdge { Id=edge.Id, SourceNodeId=edge.SourceNodeId, TargetNodeId=edge.TargetNodeId, Kind=edge.Kind switch { MacroGraphEdgeKind.True=>EdgeKind.True, MacroGraphEdgeKind.False=>EdgeKind.False, MacroGraphEdgeKind.Loop=>EdgeKind.Loop, _=>EdgeKind.Next } });
        return result;
    }
}
