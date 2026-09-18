using System.Collections.Generic;
using System.Linq;
using System;
using System.Windows.Input;
using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ApexClick.Models;

namespace ApexClick.ViewModels;

public sealed class EditorViewModel : ViewModelBase
{
    private readonly MacroGraphOptimizer _optimizer = new();
    private Guid[] _originalEventOrder = Array.Empty<Guid>();
    private MacroGraph _graph = new();
    private MacroScript? _workingScript;
    private GraphNodeViewModel? _selectedNode;
    private bool _isDirty;

    public MacroGraph Graph { get => _graph; private set => SetField(ref _graph, value); }
    public MacroScript? WorkingScript => _workingScript;

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetField(ref _isDirty, value);
    }

    public void MarkSaved() => IsDirty = false;

    public ObservableCollection<GraphNodeViewModel> Nodes { get; } = new();
    public ObservableCollection<GraphConnectionViewModel> Connections { get; } = new();
    public ObservableCollection<string> NodeIds { get; } = new();
    public ObservableCollection<string> ImageAssets { get; } = new();

    public IReadOnlyList<MacroGraphEdgeKind> EdgeKinds { get; } = Enum.GetValues<MacroGraphEdgeKind>();

    public RelayCommand<GraphNodeViewModel> MoveUpCommand { get; }
    public RelayCommand<GraphNodeViewModel> MoveDownCommand { get; }
    public RelayCommand AddMouseMoveCommand { get; }
    public RelayCommand AddClickCommand { get; }
    public RelayCommand AddDoubleClickCommand { get; }
    public RelayCommand AddRightClickCommand { get; }
    public RelayCommand AddWheelCommand { get; }
    public RelayCommand AddKeyPressCommand { get; }
    public RelayCommand AddTextCommand { get; }
    public RelayCommand AddWaitCommand { get; }
    public RelayCommand AddConditionTemplateCommand { get; }
    public RelayCommand AddConditionPixelCommand { get; }
    public RelayCommand AddConditionOcrCommand { get; }
    public RelayCommand AddLoopCommand { get; }
    public RelayCommand AddVariableCommand { get; }
    public RelayCommand AddSubScenarioCommand { get; }
    public RelayCommand ConnectNodesCommand { get; }
    public RelayCommand RemoveLastEdgeCommand { get; }

    public GraphNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        private set => SetField(ref _selectedNode, value);
    }

    private bool _isEditingEnabled;
    public bool IsEditingEnabled
    {
        get => _isEditingEnabled;
        set
        {
            if (!SetField(ref _isEditingEnabled, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public EditorViewModel()
    {
        MoveUpCommand = new RelayCommand<GraphNodeViewModel>(n => { if (n is not null) MoveUp(n); }, _ => IsEditingEnabled);
        MoveDownCommand = new RelayCommand<GraphNodeViewModel>(n => { if (n is not null) MoveDown(n); }, _ => IsEditingEnabled);
        AddMouseMoveCommand = new RelayCommand(() => AddAction(MacroEventType.MouseMove), () => IsEditingEnabled);
        AddClickCommand = new RelayCommand(() => AddClick(MouseVirtualKeys.Left), () => IsEditingEnabled);
        AddDoubleClickCommand = new RelayCommand(() => AddDoubleClick(MouseVirtualKeys.Left), () => IsEditingEnabled);
        AddRightClickCommand = new RelayCommand(() => AddClick(MouseVirtualKeys.Right), () => IsEditingEnabled);
        AddWheelCommand = new RelayCommand(() => AddWheel(120), () => IsEditingEnabled);
        AddKeyPressCommand = new RelayCommand(() => AddKeyPress(0x41), () => IsEditingEnabled);
        AddTextCommand = new RelayCommand(() => AddText("Текст"), () => IsEditingEnabled);
        AddWaitCommand = new RelayCommand(() => AddWait(1000), () => IsEditingEnabled);
        AddConditionTemplateCommand = new RelayCommand(() => AddLogicNode(MacroEventType.ConditionScreenTemplate), () => IsEditingEnabled);
        AddConditionPixelCommand = new RelayCommand(() => AddLogicNode(MacroEventType.ConditionScreenPixel), () => IsEditingEnabled);
        AddConditionOcrCommand = new RelayCommand(() => AddLogicNode(MacroEventType.ConditionOcrText), () => IsEditingEnabled);
        AddLoopCommand = new RelayCommand(() => AddLogicNode(MacroEventType.Loop), () => IsEditingEnabled);
        AddVariableCommand = new RelayCommand(() => AddLogicNode(MacroEventType.Variable), () => IsEditingEnabled);
        AddSubScenarioCommand = new RelayCommand(() => AddLogicNode(MacroEventType.SubScenarioCall), () => IsEditingEnabled);
        ConnectNodesCommand = new RelayCommand(() => { }, () => IsEditingEnabled && NodeIds.Count >= 2);
        RemoveLastEdgeCommand = new RelayCommand(RemoveLastEdge, () => IsEditingEnabled && Graph.Edges.Count > 0);
    }

    public void ConfigureGraphOptimization(bool autoSimplify, int jitterPixels, int maxPathPoints)
    {
        _optimizer.Options = new MousePathOptimizerOptions
        {
            MinimumMoveSamples = 4,
            CollinearityTolerancePixels = Math.Clamp(jitterPixels, 0, 100),
            MinimumSpatialDistancePixels = 2,
            MaxGapMs = 80,
            MaxPathPoints = Math.Clamp(maxPathPoints, 4, 128),
            SimplifyPaths = autoSimplify
        };
    }

    public void LoadFromScript(MacroScript script)
    {
        _workingScript = script;
        Nodes.Clear();
        Connections.Clear();
        NodeIds.Clear();
        ImageAssets.Clear();
        SelectedNode = null;
        _originalEventOrder = script.Events.Select(e => e.Id).ToArray();
        foreach (var asset in script.EmbeddedAssets.Keys.Where(IsImageAsset)) ImageAssets.Add(asset);

        var graph = _optimizer.Build(script);
        if (ApexClick.FeaturePack.GraphEngine.ExecutionGraphPersistence.TryLoad(script, out var persistedExecution) && persistedExecution is not null)
        {
            var nodes = graph.Nodes.ToList();
            var existing = nodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var runtime in persistedExecution.Nodes)
            {
                if (existing.Contains(runtime.Id)) continue;
                var sourceEvents = runtime.EventIds
                    .Select(id => script.Events.FirstOrDefault(e => e.Id == id))
                    .Where(e => e is not null).Cast<MacroEvent>().ToArray();
                var node = new MacroGraphNode
                {
                    Id = runtime.Id,
                    DisplayType = runtime.Type.ToString(),
                    RuntimeType = runtime.Type,
                    SourceEvents = sourceEvents,
                    Properties = runtime.Properties.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                    EditorX = GetCoordinate(script, runtime.Id, "x", 60),
                    EditorY = GetCoordinate(script, runtime.Id, "y", 40 + nodes.Count * 110)
                };
                nodes.Add(node);
                existing.Add(node.Id);
            }

            var edgeMap = new Dictionary<string, MacroGraphEdge>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in graph.Edges) edgeMap[e.Id] = e;
            foreach (var e in persistedExecution.Edges)
            {
                var kind = e.Kind switch
                {
                    ApexClick.FeaturePack.GraphEngine.EdgeKind.True => MacroGraphEdgeKind.True,
                    ApexClick.FeaturePack.GraphEngine.EdgeKind.False => MacroGraphEdgeKind.False,
                    ApexClick.FeaturePack.GraphEngine.EdgeKind.Loop => MacroGraphEdgeKind.Loop,
                    _ => MacroGraphEdgeKind.Next
                };
                edgeMap[e.Id] = new MacroGraphEdge
                {
                    Id = e.Id, SourceNodeId = e.SourceNodeId, TargetNodeId = e.TargetNodeId, Kind = kind
                };
            }
            graph = new MacroGraph { Nodes = nodes, Edges = edgeMap.Values.ToArray() };
        }

        Graph = graph;
        foreach (var graphNode in graph.Nodes)
        {
            graphNode.EditorX = GetCoordinate(script, graphNode.Id, "x", graphNode.EditorX);
            graphNode.EditorY = GetCoordinate(script, graphNode.Id, "y", graphNode.EditorY);
            AddNodeViewModel(graphNode);
        }
        RefreshConnections();
        IsDirty = false;
    }

    public MacroScript SaveToScript()
    {
        var flattened = Nodes.SelectMany(n => n.SourceEvents).ToList();
        var flattenedIds = flattened.Select(e => e.Id).ToArray();
        var unchanged = flattenedIds.SequenceEqual(_originalEventOrder);
        var script = new MacroScript();

        if (unchanged)
        {
            script.Events.AddRange(flattened);
            return script;
        }

        long cursor = 0;
        foreach (var node in Nodes)
        {
            if (node.SourceEvents.Count == 0) continue;
            var start = node.SourceEvents[0].TimestampMs;
            foreach (var original in node.SourceEvents)
            {
                var relative = Math.Max(0, original.TimestampMs - start);
                script.Events.Add(CloneEvent(original, cursor + relative));
            }
            cursor = script.Events[^1].TimestampMs + 1;
        }
        return script;
    }

    public void SelectNode(string nodeId) => SelectedNode = Nodes.FirstOrDefault(n => n.Id == nodeId);

    public void AddAction(MacroEventType type)
    {
        if (type is not (MacroEventType.MouseMove or MacroEventType.KeyDown or MacroEventType.KeyUp or MacroEventType.Wait or MacroEventType.TextInput))
            throw new ArgumentOutOfRangeException(nameof(type));

        var timestamp = NextTimestamp();
        var evt = new MacroEvent
        {
            TimestampMs = timestamp,
            Type = type,
            NormalizedX = 0.5,
            NormalizedY = 0.5,
            Payload = type == MacroEventType.Wait ? 1000 : type == MacroEventType.TextInput ? "Текст" : null,
            VirtualKeyCode = type is MacroEventType.KeyDown or MacroEventType.KeyUp ? 0x41 : null,
            ScanCode = type is MacroEventType.KeyDown or MacroEventType.KeyUp ? 30 : null
        };
        AddActionNode(type.ToString(), type, new[] { evt });
    }

    public void AddClick(int mouseButton)
    {
        var t = NextTimestamp();
        var down = new MacroEvent { TimestampMs = t, Type = MacroEventType.MouseButtonDown, NormalizedX = 0.5, NormalizedY = 0.5, VirtualKeyCode = mouseButton };
        var up = new MacroEvent { TimestampMs = t + 50, Type = MacroEventType.MouseButtonUp, NormalizedX = 0.5, NormalizedY = 0.5, VirtualKeyCode = mouseButton };
        AddActionNode(mouseButton == MouseVirtualKeys.Right ? "Right Click" : "Click", MacroEventType.MouseButtonDown, new[] { down, up });
    }

    public void AddDoubleClick(int mouseButton)
    {
        var t = NextTimestamp();
        var events = new[]
        {
            new MacroEvent { TimestampMs = t, Type = MacroEventType.MouseButtonDown, NormalizedX = 0.5, NormalizedY = 0.5, VirtualKeyCode = mouseButton },
            new MacroEvent { TimestampMs = t + 40, Type = MacroEventType.MouseButtonUp, NormalizedX = 0.5, NormalizedY = 0.5, VirtualKeyCode = mouseButton },
            new MacroEvent { TimestampMs = t + 100, Type = MacroEventType.MouseButtonDown, NormalizedX = 0.5, NormalizedY = 0.5, VirtualKeyCode = mouseButton },
            new MacroEvent { TimestampMs = t + 140, Type = MacroEventType.MouseButtonUp, NormalizedX = 0.5, NormalizedY = 0.5, VirtualKeyCode = mouseButton }
        };
        AddActionNode("Double Click", MacroEventType.MouseButtonDown, events);
    }

    public void AddWheel(int delta)
    {
        var evt = new MacroEvent { TimestampMs = NextTimestamp(), Type = MacroEventType.MouseWheel, NormalizedX = 0.5, NormalizedY = 0.5, Payload = delta };
        AddActionNode(delta > 0 ? "Scroll Up" : "Scroll Down", MacroEventType.MouseWheel, new[] { evt });
    }

    public void AddKeyPress(int virtualKey)
    {
        var t = NextTimestamp();
        var events = new[]
        {
            new MacroEvent { TimestampMs = t, Type = MacroEventType.KeyDown, VirtualKeyCode = virtualKey, ScanCode = 0 },
            new MacroEvent { TimestampMs = t + 30, Type = MacroEventType.KeyUp, VirtualKeyCode = virtualKey, ScanCode = 0 }
        };
        AddActionNode($"Key 0x{virtualKey:X2}", MacroEventType.KeyDown, events);
    }

    public void AddText(string text)
    {
        var evt = new MacroEvent { TimestampMs = NextTimestamp(), Type = MacroEventType.TextInput, Payload = text };
        AddActionNode("Type Text", MacroEventType.TextInput, new[] { evt });
    }

    public void AddWait(int milliseconds)
    {
        var evt = new MacroEvent { TimestampMs = NextTimestamp(), Type = MacroEventType.Wait, Payload = Math.Clamp(milliseconds, 1, 600_000) };
        AddActionNode("Wait", MacroEventType.Wait, new[] { evt });
    }

    public void AddLogicNode(MacroEventType type)
    {
        if (type is not (MacroEventType.ConditionScreenTemplate or MacroEventType.ConditionScreenPixel or MacroEventType.ConditionOcrText or MacroEventType.Loop or MacroEventType.Variable or MacroEventType.SubScenarioCall))
            throw new ArgumentOutOfRangeException(nameof(type));

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (type)
        {
            case MacroEventType.ConditionScreenTemplate:
                properties["expression"] = "true";
                properties["templatePath"] = "";
                properties["threshold"] = "0.90";
                break;
            case MacroEventType.ConditionScreenPixel:
                properties["expression"] = "true";
                properties["pixelX"] = "0.5";
                properties["pixelY"] = "0.5";
                properties["pixelR"] = "0";
                properties["pixelG"] = "255";
                properties["pixelB"] = "0";
                properties["pixelTolerance"] = "10";
                break;
            case MacroEventType.ConditionOcrText:
                properties["expression"] = "true";
                properties["regionX"] = "0";
                properties["regionY"] = "0";
                properties["regionWidth"] = "1";
                properties["regionHeight"] = "1";
                properties["ocrText"] = "text";
                break;
            case MacroEventType.Loop:
                properties["expression"] = "counter < 3";
                properties["counter"] = "0";
                break;
            case MacroEventType.Variable:
                properties["name"] = "variable";
                properties["value"] = "value";
                break;
            case MacroEventType.SubScenarioCall:
                properties["path"] = "";
                break;
        }
        AddGraphNodeOnly(type.ToString(), type, properties);
    }

    public void RemoveNode(string nodeId)
    {
        var target = Graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (target is null) return;
        Graph = new MacroGraph
        {
            Nodes = Graph.Nodes.Where(n => n.Id != nodeId).ToArray(),
            Edges = Graph.Edges.Where(e => e.SourceNodeId != nodeId && e.TargetNodeId != nodeId).ToArray()
        };
        var vm = Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (vm is not null) Nodes.Remove(vm);
        SelectedNode = null;
        RefreshConnections();
        IsDirty = true;
    }

    public async Task<string?> AddImageTriggerFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_workingScript is null) return null;
        var extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Поддерживаются PNG/JPG/JPEG/BMP/WebP.");

        await using var stream = File.OpenRead(path);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var assetId = $"template-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        _workingScript.EmbeddedAssets[assetId] = memory.ToArray();
        if (!ImageAssets.Contains(assetId)) ImageAssets.Add(assetId);

        var node = AddGraphNodeOnly(
            $"Image Trigger · {Path.GetFileName(path)}",
            MacroEventType.ConditionScreenTemplate,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["expression"] = "true",
                ["assetId"] = assetId,
                ["threshold"] = "0.90",
                ["templateName"] = Path.GetFileName(path)
            });
        SelectNode(node.Id);
        IsDirty = true;
        return assetId;
    }

    public MacroGraphNode AddGraphNodeOnly(string displayName, MacroEventType type, Dictionary<string, string>? properties = null)
    {
        var node = new MacroGraphNode
        {
            Id = $"node-{Guid.NewGuid():N}",
            DisplayType = displayName,
            RuntimeType = type,
            Properties = properties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            EditorX = 50 + (Nodes.Count % 3) * 300,
            EditorY = 50 + (Nodes.Count / 3) * 150
        };
        var newNodes = Graph.Nodes.Append(node).ToArray();
        var newEdges = Graph.Edges.ToList();
        if (newNodes.Length > 1)
        {
            var source = newNodes[^2];
            if (!newEdges.Any(e => e.SourceNodeId == source.Id && e.TargetNodeId == node.Id))
                newEdges.Add(new MacroGraphEdge { Id = $"edge-{Guid.NewGuid():N}", SourceNodeId = source.Id, TargetNodeId = node.Id, Kind = MacroGraphEdgeKind.Next });
        }
        Graph = new MacroGraph { Nodes = newNodes, Edges = newEdges };
        AddNodeViewModel(node);
        RefreshConnections();
        IsDirty = true;
        return node;
    }

    public void AddEdge(string sourceId, string targetId, MacroGraphEdgeKind kind)
    {
        if (sourceId == targetId) return;
        if (Graph.Nodes.All(n => n.Id != sourceId) || Graph.Nodes.All(n => n.Id != targetId)) return;
        if (Graph.Edges.Any(e => e.SourceNodeId == sourceId && e.TargetNodeId == targetId && e.Kind == kind)) return;
        var edges = Graph.Edges.Append(new MacroGraphEdge
        {
            Id = $"edge-{Guid.NewGuid():N}", SourceNodeId = sourceId, TargetNodeId = targetId, Kind = kind
        }).ToArray();
        Graph = new MacroGraph { Nodes = Graph.Nodes, Edges = edges };
        RefreshConnections();
        IsDirty = true;
    }

    public void RemoveEdge(string edgeId)
    {
        Graph = new MacroGraph { Nodes = Graph.Nodes, Edges = Graph.Edges.Where(e => e.Id != edgeId).ToArray() };
        RefreshConnections();
        IsDirty = true;
    }

    public void MoveUp(GraphNodeViewModel node)
    {
        int i = Nodes.IndexOf(node);
        if (i <= 0) return;
        Nodes.Move(i, i - 1);
        RefreshConnections();
        IsDirty = true;
    }

    public void MoveDown(GraphNodeViewModel node)
    {
        int i = Nodes.IndexOf(node);
        if (i < 0 || i >= Nodes.Count - 1) return;
        Nodes.Move(i, i + 1);
        RefreshConnections();
        IsDirty = true;
    }

    public void SetEdgeKind(string edgeId, MacroGraphEdgeKind kind)
    {
        var edges = Graph.Edges.Select(e => e.Id == edgeId
            ? e with { Kind = kind }
            : e).ToArray();
        Graph = new MacroGraph { Nodes = Graph.Nodes, Edges = edges };
        RefreshConnections();
        IsDirty = true;
    }

    public void SetNodeProperty(string nodeId, string key, string value)
    {
        var node = Graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;
        node.Properties[key] = value;
        OnPropertyChanged(nameof(Graph));
        IsDirty = true;
    }

    public void ReplaceNodePayload(string nodeId, string value)
    {
        var node = Graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || node.SourceEvents.Count == 0) return;
        var source = node.SourceEvents.ToArray();
        var replaced = source.Select((e, i) => i == 0 ? new MacroEvent
        {
            Id = e.Id, TimestampMs = e.TimestampMs, Type = e.Type, NormalizedX = e.NormalizedX, NormalizedY = e.NormalizedY,
            VirtualKeyCode = e.VirtualKeyCode, ScanCode = e.ScanCode, KeyboardLayoutId = e.KeyboardLayoutId, Payload = value
        } : e).ToArray();
        var newNodes = Graph.Nodes.Select(n => n.Id == nodeId ? new MacroGraphNode
        {
            Id = n.Id, DisplayType = n.DisplayType, RuntimeType = n.RuntimeType, SourceEvents = replaced, PathMode = n.PathMode,
            PathPoints = n.PathPoints, Properties = n.Properties, EditorX = n.EditorX, EditorY = n.EditorY
        } : n).ToArray();
        Graph = new MacroGraph { Nodes = newNodes, Edges = Graph.Edges };
        IsDirty = true;
        var vm = Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (vm is not null) Nodes[Nodes.IndexOf(vm)] = new GraphNodeViewModel(newNodes.First(n => n.Id == nodeId)) { CanvasX = vm.CanvasX, CanvasY = vm.CanvasY };
    }

    public void SetNodePosition(string nodeId, double x, double y)
    {
        var graphNode = Graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (graphNode is not null)
        {
            graphNode.EditorX = Math.Max(0, x);
            graphNode.EditorY = Math.Max(0, y);
        }
        var vm = Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (vm is not null)
        {
            vm.CanvasX = Math.Max(0, x);
            vm.CanvasY = Math.Max(0, y);
        }
        IsDirty = true;
    }

    public ApexClick.FeaturePack.GraphEngine.ExecutionGraph BuildExecutionGraph() => ApexClick.FeaturePack.GraphEngine.ExecutionGraphPersistence.FromMacroGraph(Graph);

    private void AddActionNode(string displayName, MacroEventType runtimeType, IReadOnlyList<MacroEvent> events)
    {
        var node = new MacroGraphNode
        {
            Id = $"action-{Guid.NewGuid():N}",
            DisplayType = displayName,
            RuntimeType = runtimeType,
            SourceEvents = events,
            EditorX = 50 + (Nodes.Count % 3) * 300,
            EditorY = 50 + (Nodes.Count / 3) * 150
        };
        var nodes = Graph.Nodes.Append(node).ToArray();
        var edges = Graph.Edges.ToList();
        if (nodes.Length > 1)
            edges.Add(new MacroGraphEdge { Id = $"edge-{Guid.NewGuid():N}", SourceNodeId = nodes[^2].Id, TargetNodeId = node.Id, Kind = MacroGraphEdgeKind.Next });
        Graph = new MacroGraph { Nodes = nodes, Edges = edges };
        AddNodeViewModel(node);
        RefreshConnections();
        SelectedNode = Nodes[^1];
    }

    private long NextTimestamp() => Nodes.SelectMany(n => n.SourceEvents).Select(e => e.TimestampMs).DefaultIfEmpty(-1).Max() + 1;

    private void AddNodeViewModel(MacroGraphNode graphNode)
    {
        var node = new GraphNodeViewModel(graphNode) { CanvasX = graphNode.EditorX, CanvasY = graphNode.EditorY };
        Nodes.Add(node);
        NodeIds.Clear();
        foreach (var id in Graph.Nodes.Select(n => n.Id)) NodeIds.Add(id);
    }

    private void RefreshConnections()
    {
        Connections.Clear();
        foreach (var edge in Graph.Edges)
        {
            var from = Nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
            var to = Nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId);
            if (from is not null && to is not null)
                Connections.Add(new GraphConnectionViewModel { From = from, To = to, EdgeId = edge.Id, Kind = edge.Kind });
        }
        NodeIds.Clear();
        foreach (var id in Graph.Nodes.Select(n => n.Id)) NodeIds.Add(id);
        RemoveLastEdgeCommand.RaiseCanExecuteChanged();
    }

    private void RemoveLastEdge()
    {
        if (Graph.Edges.Count == 0) return;
        RemoveEdge(Graph.Edges[^1].Id);
    }

    private static bool IsImageAsset(string id) => id.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    private static MacroEvent CloneEvent(MacroEvent original, long timestamp) => new()
    {
        Id = original.Id,
        TimestampMs = timestamp,
        Type = original.Type,
        NormalizedX = original.NormalizedX,
        NormalizedY = original.NormalizedY,
        VirtualKeyCode = original.VirtualKeyCode,
        ScanCode = original.ScanCode,
        KeyboardLayoutId = original.KeyboardLayoutId,
        Payload = original.Payload
    };

    private static double GetCoordinate(MacroScript script, string id, string axis, double fallback)
    {
        return script.Metadata.TryGetValue($"graph.node.{id}.{axis}", out var raw) &&
               double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result : fallback;
    }
}

public sealed class GraphNodeViewModel : ViewModelBase
{
    public GraphNodeViewModel(MacroGraphNode source)
    {
        Id = source.Id;
        DisplayType = source.DisplayType;
        SourceEvents = source.SourceEvents;
        PathMode = source.PathMode;
        PathPoints = source.PathPoints ?? Array.Empty<MousePathPoint>();
        RuntimeType = source.RuntimeType;
        Properties = source.Properties;
        PathPreviewPoints = BuildPathPreview(PathPoints);
        Detail = IsMousePath && PathPoints.Count > 1
            ? $"{PathMode} · {PathPoints.Count} точек · {PathPoints[^1].OffsetMs} ms"
            : SourceEvents.Count > 0
                ? $"Событий: {SourceEvents.Count}"
                : "Логический узел — двойной клик для свойств";
    }

    public string Id { get; }
    public string DisplayType { get; }
    public IReadOnlyList<MacroEvent> SourceEvents { get; }
    public IReadOnlyList<MousePathPoint> PathPoints { get; }
    public MousePathMode? PathMode { get; }
    public MacroEventType RuntimeType { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }
    public bool IsMousePath => PathMode is not null;
    public string Detail { get; }
    public PointCollection PathPreviewPoints { get; }
    public double CanvasX { get; set; }
    public double CanvasY { get; set; }

    private static PointCollection BuildPathPreview(IReadOnlyList<MousePathPoint> points)
    {
        var result = new PointCollection();
        if (points.Count == 0) return result;
        var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
        var dx = Math.Max(maxX - minX, 1e-9); var dy = Math.Max(maxY - minY, 1e-9);
        foreach (var p in points)
            result.Add(new Point((p.X - minX) / dx * 220 + 2, 42 - (p.Y - minY) / dy * 36));
        return result;
    }
}

public sealed class GraphConnectionViewModel
{
    public required GraphNodeViewModel From { get; init; }
    public required GraphNodeViewModel To { get; init; }
    public required string EdgeId { get; init; }
    public MacroGraphEdgeKind Kind { get; init; }
}
