using ApexClick.Models;

namespace ApexClick.Models.Tests;

public sealed class ExecutionGraphTests
{
    [Fact]
    public void MacroGraphOptimizer_PreservesMousePathGrouping()
    {
        var script=new MacroScript();
        for(var i=0;i<100;i++) script.Events.Add(new MacroEvent{TimestampMs=i,Type=MacroEventType.MouseMove,NormalizedX=i/99d,NormalizedY=.5});
        var graph=new MacroGraphOptimizer().Build(script);
        Assert.Single(graph.Nodes); Assert.Equal("MousePath",graph.Nodes[0].DisplayType);
        Assert.Equal(100,graph.Nodes[0].SourceEvents.Count);
    }
}
