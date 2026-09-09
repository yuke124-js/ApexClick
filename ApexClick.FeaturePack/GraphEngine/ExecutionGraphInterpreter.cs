using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using ApexClick.Models;
using ApexClick.Services.Playback;
using ApexClick.Services.Vision;

namespace ApexClick.FeaturePack.GraphEngine;

public sealed class GraphExecutionOptions
{
    public int MaxSteps { get; init; } = 100_000;
    public int MaxLoopIterationsPerNode { get; init; } = 10_000;
    public int MaxSubScenarioDepth { get; init; } = 32;
}

public interface IGraphConditionEvaluator
{
    Task<bool> EvaluateAsync(ExecutionNode node, MacroScript script, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken);
}

public sealed class DefaultGraphConditionEvaluator : IGraphConditionEvaluator
{
    private static readonly Regex Comparison = new(
        @"^(?<left>.+?)\s*(?<op>==|!=|>=|<=|>|<|contains|startsWith|endsWith)\s*(?<right>.+?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public Task<bool> EvaluateAsync(ExecutionNode node, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
        => EvaluateAsync(node, new MacroScript { Id = Guid.Empty, Name = string.Empty }, variables, cancellationToken);

    public Task<bool> EvaluateAsync(ExecutionNode node, Dictionary<string, string> variables, CancellationToken cancellationToken)
        => EvaluateAsync(node, (IReadOnlyDictionary<string, string>)variables, cancellationToken);

    public Task<bool> EvaluateAsync(ExecutionNode node, IDictionary<string, string> variables, CancellationToken cancellationToken)
        => EvaluateAsync(node, (IReadOnlyDictionary<string, string>)variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase), cancellationToken);

    public Task<bool> EvaluateAsync(ExecutionNode node, MacroScript script, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!node.Properties.TryGetValue("expression", out var expression) || string.IsNullOrWhiteSpace(expression))
            throw new InvalidDataException($"Condition/Loop node '{node.Id}' has no expression.");
        expression = PlaceholderEngine.Replace(expression, variables).Trim();
        if (bool.TryParse(expression, out var b)) return Task.FromResult(b);
        var m = Comparison.Match(expression);
        if (!m.Success)
            return Task.FromResult(variables.TryGetValue(expression, out var v) &&
                                   !string.IsNullOrWhiteSpace(v) &&
                                   !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) && v != "0");
        var left = Operand(m.Groups["left"].Value, variables);
        var right = Operand(m.Groups["right"].Value, variables);
        var op = m.Groups["op"].Value.ToLowerInvariant();
        if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var ln) &&
            double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rn))
        {
            return Task.FromResult(op switch
            {
                "==" => ln == rn, "!=" => ln != rn, ">" => ln > rn, ">=" => ln >= rn,
                "<" => ln < rn, "<=" => ln <= rn, _ => false
            });
        }
        return Task.FromResult(op switch
        {
            "==" => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            "!=" => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "startswith" => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
            "endswith" => left.EndsWith(right, StringComparison.OrdinalIgnoreCase),
            _ => false
        });
    }

    private static string Operand(string value, IReadOnlyDictionary<string, string> variables)
    {
        value = value.Trim();
        if ((value.StartsWith("\"") && value.EndsWith("\"")) || (value.StartsWith("'") && value.EndsWith("'"))) return value[1..^1];
        return variables.TryGetValue(value, out var v) ? v : value;
    }
}

public sealed class VisionGraphConditionEvaluator : IGraphConditionEvaluator
{
    private readonly DefaultGraphConditionEvaluator _fallback = new();
    private readonly ScreenCaptureService _capture;
    private readonly ImageAnalysisService _vision;

    public VisionGraphConditionEvaluator(ScreenCaptureService capture, ImageAnalysisService vision)
    {
        _capture = capture; _vision = vision;
    }

    public async Task<bool> EvaluateAsync(ExecutionNode node, MacroScript script, IReadOnlyDictionary<string, string> variables, CancellationToken ct)
    {
        if (node.Type == MacroEventType.ConditionScreenTemplate)
        {
            var frame = await _capture.CaptureFrameAsync(ct);
            var threshold = ParseDouble(node, "threshold", 0.9);
            if (node.Properties.TryGetValue("assetId", out var assetId) &&
                script.EmbeddedAssets.TryGetValue(assetId, out var assetBytes))
            {
                if (_capture.Mode == ScreenCaptureService.CaptureMode.SpecificWindow &&
                    _capture.TryGetTargetWindowRect(out var windowRect))
                    return (await _vision.FindTemplateInWindowAsync(frame, assetBytes, threshold, windowRect, _capture.GetVirtualScreenBounds(), ct)).Found;
                return (await _vision.FindTemplateAsync(frame, assetBytes, threshold, ct)).Found;
            }
            var path = Required(node, "templatePath", variables);
            if (_capture.Mode == ScreenCaptureService.CaptureMode.SpecificWindow &&
                _capture.TryGetTargetWindowRect(out var targetRect))
                return (await _vision.FindTemplateInWindowAsync(frame, path, threshold, targetRect, _capture.GetVirtualScreenBounds(), ct)).Found;
            return (await _vision.FindTemplateAsync(frame, path, threshold, ct)).Found;
        }
        if (node.Type == MacroEventType.ConditionScreenPixel)
        {
            var frame = await _capture.CaptureFrameAsync(ct);
            var x = ParseDouble(node, "pixelX"); var y = ParseDouble(node, "pixelY");
            var r = (byte)Math.Clamp(ParseDouble(node, "pixelR"), 0, 255);
            var g = (byte)Math.Clamp(ParseDouble(node, "pixelG"), 0, 255);
            var b = (byte)Math.Clamp(ParseDouble(node, "pixelB"), 0, 255);
            var tolerance = (int)Math.Clamp(ParseDouble(node, "pixelTolerance", 10), 0, 255);
            return await _vision.CheckPixelAsync(frame, x, y, (r, g, b), tolerance, ct);
        }
        if (node.Type == MacroEventType.ConditionOcrText)
        {
            var frame = await _capture.CaptureFrameAsync(ct);
            var region = new NormalizedRegion(
                ParseDouble(node, "regionX"), ParseDouble(node, "regionY"),
                ParseDouble(node, "regionWidth", 1), ParseDouble(node, "regionHeight", 1));
            var expected = Required(node, "ocrText", variables);
            var text = await _vision.ReadTextAsync(frame, region, ct);
            return text.Contains(expected, StringComparison.OrdinalIgnoreCase);
        }
        return await _fallback.EvaluateAsync(node, script, variables, ct);
    }

    private static string Required(ExecutionNode node, string key, IReadOnlyDictionary<string,string> vars) =>
        node.Properties.TryGetValue(key, out var v) ? PlaceholderEngine.Replace(v, vars) : throw new InvalidDataException($"Node '{node.Id}' missing '{key}'.");
    private static double ParseDouble(ExecutionNode n,string k,double d=0) => n.Properties.TryGetValue(k,out var v) && double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out var x) ? x : d;
}

public interface ISubScenarioLoader
{
    Task<MacroScript> LoadAsync(string path, CancellationToken cancellationToken);
}

public sealed class MscrSubScenarioLoader : ISubScenarioLoader
{
    public Task<MacroScript> LoadAsync(string path, CancellationToken cancellationToken) => MscrScriptSerializer.LoadAsync(path, cancellationToken);
}

public sealed class ExecutionGraphInterpreter
{
    private readonly PlaybackEngine _playbackEngine;
    private readonly IGraphConditionEvaluator _conditionEvaluator;
    private readonly ISubScenarioLoader _subScenarioLoader;
    private readonly GraphExecutionOptions _options;

    public ExecutionGraphInterpreter(PlaybackEngine playbackEngine, IGraphConditionEvaluator conditionEvaluator,
        ISubScenarioLoader subScenarioLoader, GraphExecutionOptions? options = null)
    {
        _playbackEngine = playbackEngine ?? throw new ArgumentNullException(nameof(playbackEngine));
        _conditionEvaluator = conditionEvaluator ?? throw new ArgumentNullException(nameof(conditionEvaluator));
        _subScenarioLoader = subScenarioLoader ?? throw new ArgumentNullException(nameof(subScenarioLoader));
        _options = options ?? new GraphExecutionOptions();
    }

    public async Task ExecuteScriptGraphAsync(MacroScript script, ExecutionGraph graph,
        IDictionary<string,string>? variables = null, CancellationToken cancellationToken = default)
        => await ExecuteCoreAsync(script, graph, variables ?? new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, cancellationToken);

    private async Task ExecuteCoreAsync(MacroScript script, ExecutionGraph graph, IDictionary<string,string> variables,
        HashSet<string> callStack, int depth, CancellationToken ct)
    {
        if (depth > _options.MaxSubScenarioDepth) throw new InvalidOperationException("Превышена глубина вложенных подсценариев.");
        ValidateGraph(graph, script.Events.ToDictionary(e=>e.Id));
        if (!callStack.Add(script.Id.ToString("N"))) throw new InvalidOperationException("Обнаружена рекурсивная ссылка на подсценарий.");
        try
        {
            var byId = script.Events.ToDictionary(e=>e.Id);
            var loopCounts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            string? current = graph.Nodes.FirstOrDefault()?.Id;
            int steps=0;
            long? previousTimestamp=null;
            while (current is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (++steps > _options.MaxSteps) throw new InvalidOperationException("Превышен MaxSteps.");
                var node = graph.Find(current) ?? throw new InvalidDataException($"Узел '{current}' не найден.");
                if (node.Type is MacroEventType.ConditionScreenTemplate or MacroEventType.ConditionScreenPixel or MacroEventType.ConditionOcrText)
                {
                    current = await BranchAsync(graph,node.Id,await EvaluateConditionAsync(node, script, ToReadOnlyVariables(variables), ct));
                    continue;
                }
                if (node.Type == MacroEventType.Loop)
                {
                    var count = loopCounts.GetValueOrDefault(node.Id);
                    if (count >= _options.MaxLoopIterationsPerNode) throw new InvalidOperationException($"Loop '{node.Id}' превысил лимит.");
                    if (await EvaluateConditionAsync(node, script, ToReadOnlyVariables(variables), ct)) { loopCounts[node.Id]=count+1; current=graph.Outgoing(node.Id).FirstOrDefault(e=>e.Kind==EdgeKind.Loop)?.TargetNodeId ?? Next(graph,node.Id); }
                    else { loopCounts.Remove(node.Id); current=await BranchAsync(graph,node.Id,false); }
                    continue;
                }
                if (node.Type == MacroEventType.Variable) { ApplyVariable(node,variables); current=Next(graph,node.Id); continue; }
                if (node.Type == MacroEventType.SubScenarioCall)
                {
                    var raw = node.Properties.TryGetValue("path",out var path) ? PlaceholderEngine.Replace(path,variables) : throw new InvalidDataException("SubScenarioCall missing path.");
                    var child=await _subScenarioLoader.LoadAsync(raw,ct);
                    if (ExecutionGraphPersistence.TryLoad(child,out var childGraph) && childGraph is not null)
                        await ExecuteCoreAsync(child,childGraph,variables,callStack,depth+1,ct);
                    else
                        await _playbackEngine.PlayAsync(child,ct);
                    current=Next(graph,node.Id); continue;
                }
                if (node.EventIds.Count>0)
                {
                    var first=node.EventIds.Select(id=>byId[id]).MinBy(e=>e.TimestampMs) ?? throw new InvalidDataException("Пустая группа событий.");
                    if (previousTimestamp is { } prev && first.TimestampMs>prev)
                    {
                        var wait=new MacroScript { Id=script.Id, Name=script.Name, PlaybackSpeedMultiplier=script.PlaybackSpeedMultiplier };
                        foreach(var meta in script.Metadata) wait.Metadata[meta.Key]=meta.Value;
                        wait.Events.Add(new MacroEvent{Type=MacroEventType.Wait,TimestampMs=0,Payload=first.TimestampMs-prev});
                        await _playbackEngine.PlayAsync(wait,ct);
                    }
                    var group=new MacroScript{Id=script.Id,Name=script.Name,PlaybackSpeedMultiplier=script.PlaybackSpeedMultiplier};
                    foreach(var meta in script.Metadata) group.Metadata[meta.Key]=meta.Value;
                    foreach(var id in node.EventIds)
                    {
                        var e=byId[id];
                        group.Events.Add(new MacroEvent{Id=e.Id,TimestampMs=e.TimestampMs-first.TimestampMs,Type=e.Type,NormalizedX=e.NormalizedX,NormalizedY=e.NormalizedY,VirtualKeyCode=e.VirtualKeyCode,ScanCode=e.ScanCode,KeyboardLayoutId=e.KeyboardLayoutId,Payload=PlaceholderEngine.ReplacePayload(e.Payload, ToReadOnlyVariables(variables))});
                    }
                    await _playbackEngine.PlayAsync(group,ct);
                    previousTimestamp=node.EventIds.Select(id=>byId[id].TimestampMs).Max();
                }
                current=Next(graph,node.Id);
            }
        }
        finally { callStack.Remove(script.Id.ToString("N")); }
    }

    private static IReadOnlyDictionary<string, string> ToReadOnlyVariables(IDictionary<string, string> variables) =>
        variables is IReadOnlyDictionary<string, string> readOnly
            ? readOnly
            : new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);

    private async Task<bool> EvaluateConditionAsync(ExecutionNode node, MacroScript script, IReadOnlyDictionary<string,string> variables, CancellationToken ct)
    {
        if (node.Type == MacroEventType.ConditionScreenTemplate &&
            node.Properties.TryGetValue("assetId", out var assetId) &&
            script.EmbeddedAssets.TryGetValue(assetId, out var bytes))
        {
            var extension = Path.GetExtension(assetId);
            var tempPath = Path.Combine(Path.GetTempPath(), "ApexClickTemplates", $"{Guid.NewGuid():N}{extension}");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes, ct);
                var clone = new ExecutionNode { Id = node.Id, Type = node.Type };
                foreach (var pair in node.Properties) clone.Properties[pair.Key] = pair.Value;
                clone.Properties["templatePath"] = tempPath;
                return await _conditionEvaluator.EvaluateAsync(clone, script, variables, ct);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
        return await _conditionEvaluator.EvaluateAsync(node, script, variables, ct);
    }

    private static Task<string?> BranchAsync(ExecutionGraph graph,string id,bool value) => Task.FromResult(graph.Outgoing(id).FirstOrDefault(e=>e.Kind==(value?EdgeKind.True:EdgeKind.False))?.TargetNodeId ?? Next(graph,id));
    private static string? Next(ExecutionGraph graph,string id)=>graph.Outgoing(id).FirstOrDefault(e=>e.Kind==EdgeKind.Next)?.TargetNodeId;
    private static void ApplyVariable(ExecutionNode n, IDictionary<string,string> vars){ if(!n.Properties.TryGetValue("name",out var name)) throw new InvalidDataException("Variable node missing name."); var value=n.Properties.TryGetValue("value",out var v)?PlaceholderEngine.Replace(v,vars):string.Empty; vars[name]=value; }
    private static void ValidateGraph(ExecutionGraph g,IReadOnlyDictionary<Guid,MacroEvent> ev)
    {
        var ids=g.Nodes.Select(n=>n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var e in g.Edges) if(!ids.Contains(e.SourceNodeId)||!ids.Contains(e.TargetNodeId)) throw new InvalidDataException($"Edge '{e.Id}' references a missing node.");
        foreach(var n in g.Nodes) foreach(var id in n.EventIds) if(!ev.ContainsKey(id) && n.Type is not (MacroEventType.ConditionScreenTemplate or MacroEventType.ConditionScreenPixel or MacroEventType.ConditionOcrText or MacroEventType.Loop or MacroEventType.Variable or MacroEventType.SubScenarioCall)) throw new InvalidDataException($"Node '{n.Id}' references missing event '{id}'.");
    }
}
