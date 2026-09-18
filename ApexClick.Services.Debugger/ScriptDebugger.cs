using ApexClick.Models;

namespace ApexClick.Services.Debugger;

public sealed class ScriptDebugger : IScriptDebugger
{
    private MacroScript? _script;
    private int _index=-1;
    private int _graphIndex=-1;
    private readonly HashSet<Guid> _breakpoints=new();
    private readonly Dictionary<string,string> _variables=new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MacroGraphNode>? _graphNodes;

    public void Load(MacroScript script){_script=script;_index=-1;_graphIndex=-1;_variables.Clear();_graphNodes=null;}
    public void LoadGraph(MacroScript script, FeaturePackGraphAdapter? adapter=null){Load(script); _graphNodes=adapter?.Build(script) ?? new MacroGraphOptimizer().Build(script).Nodes;}
    public void SetBreakpoint(Guid eventId,bool enabled=true){if(enabled)_breakpoints.Add(eventId);else _breakpoints.Remove(eventId);}
    public bool ShouldBreak(Guid eventId)=>_breakpoints.Contains(eventId);
    public MacroEvent? Step(){ if(_script is null || _index+1>=_script.Events.Count) return null; _index++; return _script.Events[_index]; }
    public bool StepGraph(){ if(_graphNodes is null || ++_graphIndex>=_graphNodes.Count) return false; return true; }
    public MacroEvent? Current=>_script is not null&&_index>=0&&_index<_script.Events.Count?_script.Events[_index]:null;
    public MacroGraphNode? CurrentGraphNode=>_graphNodes is not null&&_graphIndex>=0&&_graphIndex<_graphNodes.Count?_graphNodes[_graphIndex]:null;
    public IReadOnlyDictionary<string,string> Variables=>_variables;
    public void SetVariable(string name,string value)=>_variables[name]=value;
    public void Reset(){_index=-1;_graphIndex=-1;_variables.Clear();}
}
