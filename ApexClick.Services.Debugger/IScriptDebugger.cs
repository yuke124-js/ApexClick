using ApexClick.Models;

namespace ApexClick.Services.Debugger;

public interface IScriptDebugger
{
    void Load(MacroScript script);
    void SetBreakpoint(Guid eventId, bool enabled = true);
    bool ShouldBreak(Guid eventId);
    MacroEvent? Step();
    MacroEvent? Current { get; }
    IReadOnlyDictionary<string,string> Variables { get; }
    void SetVariable(string name,string value);
    void Reset();
    void LoadGraph(MacroScript script, FeaturePackGraphAdapter? adapter = null);
    MacroGraphNode? CurrentGraphNode { get; }
    bool StepGraph();
}

public interface FeaturePackGraphAdapter
{
    IReadOnlyList<MacroGraphNode> Build(MacroScript script);
}
