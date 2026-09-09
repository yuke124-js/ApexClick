using ApexClick.FeaturePack.GraphEngine;
using ApexClick.Models;

namespace ApexClick.FeaturePack.Tests;

public sealed class ExecutionGraphInterpreterTests
{
    [Fact]
    public void ConditionExpressionSupportsNumericAndStringComparisons()
    {
        var evaluator = new DefaultGraphConditionEvaluator();
        var node = new ExecutionNode { Id = "c", Type = MacroEventType.Variable };
        node.Properties["expression"] = "${count} >= 3";
        var result = evaluator.EvaluateAsync(node, new Dictionary<string,string> { ["count"] = "4" }, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result);

        node.Properties["expression"] = "${name} contains click";
        result = evaluator.EvaluateAsync(node, new Dictionary<string,string> { ["name"] = "left-click" }, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result);
    }

    [Fact]
    public void PersistedExecutionGraphRoundTripsThroughMetadata()
    {
        var script = new MacroScript { Name = "graph" };
        var graph = new ExecutionGraph();
        graph.Nodes.Add(new ExecutionNode { Id = "a", Type = MacroEventType.Variable });
        graph.Nodes.Add(new ExecutionNode { Id = "b", Type = MacroEventType.Wait });
        graph.Edges.Add(new ExecutionEdge { Id = "e", SourceNodeId = "a", TargetNodeId = "b", Kind = EdgeKind.Next });

        ExecutionGraphPersistence.Save(script, graph);
        Assert.True(ExecutionGraphPersistence.TryLoad(script, out var loaded));
        Assert.Equal(2, loaded!.Nodes.Count);
        Assert.Equal(EdgeKind.Next, loaded.Edges[0].Kind);
    }
}
