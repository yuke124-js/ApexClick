using System.Collections.Generic;
using ApexClick.Models;

namespace ApexClick.FeaturePack.GraphEngine;

public enum EdgeKind { Next, True, False, Loop }

public sealed class ExecutionGraph
{
    public List<ExecutionNode> Nodes { get; } = new();
    public List<ExecutionEdge> Edges { get; } = new();
    public ExecutionNode? Find(string id) => Nodes.FirstOrDefault(x => x.Id == id);
    public IEnumerable<ExecutionEdge> Outgoing(string id) => Edges.Where(x => x.SourceNodeId == id);
}

public sealed class ExecutionNode
{
    public required string Id { get; init; }
    public required MacroEventType Type { get; init; }
    public List<Guid> EventIds { get; } = new();
    public Dictionary<string,string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ExecutionEdge
{
    public required string Id { get; init; }
    public required string SourceNodeId { get; init; }
    public required string TargetNodeId { get; init; }
    public EdgeKind Kind { get; init; } = EdgeKind.Next;
}

public static class PlaceholderEngine
{
    private static readonly System.Text.RegularExpressions.Regex Token =
        new(@"\$\{(?<name>[A-Za-z0-9_.-]+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string Replace(string value, IReadOnlyDictionary<string,string> vars) =>
        Token.Replace(value, m => vars.TryGetValue(m.Groups["name"].Value, out var v) ? v : m.Value);

    public static string Replace(string value, IDictionary<string,string> vars) => Replace(value, (IReadOnlyDictionary<string,string>)vars);

    public static object? ReplacePayload(object? payload, IReadOnlyDictionary<string,string> vars) =>
        payload is string s ? Replace(s, vars) : payload;
}
