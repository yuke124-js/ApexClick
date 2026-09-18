using ApexClick.FeaturePack.GraphEngine;
using ApexClick.Models;

namespace ApexClick.FeaturePack.Tests;

public sealed class GraphImageTriggerTests
{
    [Fact]
    public void MacroGraphPersistsTemplateAssetReference()
    {
        var script = new MacroScript { Name = "img" };
        script.EmbeddedAssets["template-a.png"] = [1, 2, 3];
        var graph = new MacroGraph
        {
            Nodes = new[]
            {
                new MacroGraphNode
                {
                    Id = "img",
                    DisplayType = "Image Trigger",
                    RuntimeType = MacroEventType.ConditionScreenTemplate,
                    Properties = new Dictionary<string,string>
                    {
                        ["assetId"] = "template-a.png",
                        ["threshold"] = "0.9"
                    }
                }
            }
        };
        var execution = ExecutionGraphPersistence.FromMacroGraph(graph);
        Assert.Equal("template-a.png", execution.Nodes[0].Properties["assetId"]);
        ExecutionGraphPersistence.Save(script, execution);
        Assert.True(ExecutionGraphPersistence.TryLoad(script, out var loaded));
        Assert.Equal("template-a.png", loaded!.Nodes[0].Properties["assetId"]);
    }
}
