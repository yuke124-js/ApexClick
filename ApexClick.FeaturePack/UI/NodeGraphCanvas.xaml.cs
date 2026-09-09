using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ApexClick.Models;

namespace ApexClick.FeaturePack.UI;

public sealed class NodeEditRequestedEventArgs : EventArgs
{
    public required string NodeId { get; init; }
}

public sealed class EdgeKindChangedEventArgs : EventArgs
{
    public required string EdgeId { get; init; }
    public required MacroGraphEdgeKind Kind { get; init; }
}

public sealed class NodeSelectionChangedEventArgs : EventArgs
{
    public string? NodeId { get; init; }
}

public sealed class NodePositionChangedEventArgs : EventArgs
{
    public required string NodeId { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}

public partial class NodeGraphCanvas : UserControl
{

    public static readonly DependencyProperty SnapToGridProperty = DependencyProperty.Register(nameof(SnapToGrid), typeof(bool), typeof(NodeGraphCanvas), new PropertyMetadata(false));
    public static readonly DependencyProperty GridSizeProperty = DependencyProperty.Register(nameof(GridSize), typeof(double), typeof(NodeGraphCanvas), new PropertyMetadata(16d));
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(NodeGraphCanvas), new PropertyMetadata(1d, OnZoomChanged));

    public bool SnapToGrid { get => (bool)GetValue(SnapToGridProperty); set => SetValue(SnapToGridProperty, value); }
    public double GridSize { get => (double)GetValue(GridSizeProperty); set => SetValue(GridSizeProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NodeGraphCanvas canvas) return;
        var scale = Math.Clamp((double)e.NewValue, 0.25, 3.0);
        canvas.Canvas.LayoutTransform = new ScaleTransform(scale, scale);
    }

    public static readonly DependencyProperty GraphProperty =
        DependencyProperty.Register(nameof(Graph), typeof(MacroGraph), typeof(NodeGraphCanvas),
            new PropertyMetadata(null, OnGraphChanged));

    public MacroGraph? Graph
    {
        get => (MacroGraph?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public event EventHandler<NodePositionChangedEventArgs>? NodePositionChanged;
    public event EventHandler<EdgeKindChangedEventArgs>? EdgeKindChanged;
    public event EventHandler<NodeEditRequestedEventArgs>? NodeEditRequested;
    public event EventHandler<NodeSelectionChangedEventArgs>? NodeSelectionChanged;

    private readonly Dictionary<string, Border> _nodeViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Line> _edgeViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Polygon> _arrowViews = new(StringComparer.Ordinal);
    private Dictionary<string, MacroGraphNode> _nodeById = new(StringComparer.Ordinal);
    private Dictionary<string, MacroGraphEdge> _edgeById = new(StringComparer.Ordinal);
    private Dictionary<string, List<MacroGraphEdge>> _edgesByNodeId = new(StringComparer.Ordinal);
    private const double ViewportBufferScreens = 3.0;
    private const int MaxRenderedNodes = 220;
    private const int MaxRenderedEdges = 320;
    private string? _draggingId;
    private bool _virtualizing;
    private Point _dragOffset;
    private Brush? _fogWhiteBrush, _steelGreyBrush, _signalBlueBrush, _slatePanelBrush;
    private double _lastRefreshLeft = double.NaN, _lastRefreshTop = double.NaN, _lastRefreshRight, _lastRefreshBottom;

    public NodeGraphCanvas() => InitializeComponent();

    private Brush FogWhiteBrush => _fogWhiteBrush ??= (Brush)FindResource("FogWhiteBrush");
    private Brush SteelGreyBrush => _steelGreyBrush ??= (Brush)FindResource("SteelGreyBrush");
    private Brush SignalBlueBrush => _signalBlueBrush ??= (Brush)FindResource("SignalBlueBrush");
    private Brush SlatePanelBrush => _slatePanelBrush ??= (Brush)FindResource("SlatePanelBrush");

    private static void OnGraphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NodeGraphCanvas canvas) canvas.Rebuild();
    }

    public void Rebuild()
    {
        UpdateCanvasExtent();
        RefreshVirtualizedVisuals(true);
    }

    private void UpdateCanvasExtent()
    {
        _nodeById = Graph?.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal) ?? new Dictionary<string, MacroGraphNode>(StringComparer.Ordinal);
        _edgeById = Graph?.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal) ?? new Dictionary<string, MacroGraphEdge>(StringComparer.Ordinal);
        _edgesByNodeId = new Dictionary<string, List<MacroGraphEdge>>(StringComparer.Ordinal);
        if (Graph is not null)
        {
            foreach (var edge in Graph.Edges)
            {
                if (!_edgesByNodeId.TryGetValue(edge.SourceNodeId, out var sourceEdges))
                    _edgesByNodeId[edge.SourceNodeId] = sourceEdges = new List<MacroGraphEdge>();
                if (!_edgesByNodeId.TryGetValue(edge.TargetNodeId, out var targetEdges))
                    _edgesByNodeId[edge.TargetNodeId] = targetEdges = new List<MacroGraphEdge>();
                sourceEdges.Add(edge);
                if (!ReferenceEquals(sourceEdges, targetEdges)) targetEdges.Add(edge);
            }
        }
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            Canvas.Width = Math.Max(800, Viewport.ActualWidth);
            Canvas.Height = Math.Max(600, Viewport.ActualHeight);
            return;
        }

        double maxX = Graph.Nodes.Max(n => n.EditorX) + 360;
        double maxY = Graph.Nodes.Max(n => n.EditorY) + 220;
        Canvas.Width = Math.Max(1200, maxX);
        Canvas.Height = Math.Max(800, maxY);
    }

    private void RefreshVirtualizedVisuals(bool force)
    {
        if (_virtualizing || Graph is null) return;
        _virtualizing = true;
        try
        {
            double vw = Viewport.ViewportWidth;
            double vh = Viewport.ViewportHeight;
            if (double.IsNaN(vw) || vw <= 0) vw = 900;
            if (double.IsNaN(vh) || vh <= 0) vh = 600;

            double bufferX = vw * ViewportBufferScreens;
            double bufferY = vh * ViewportBufferScreens;
            var left = Math.Max(0, Viewport.HorizontalOffset - bufferX);
            var top = Math.Max(0, Viewport.VerticalOffset - bufferY);
            var right = Viewport.HorizontalOffset + vw + bufferX;
            var bottom = Viewport.VerticalOffset + vh + bufferY;

            if (!force)
            {
                var viewLeft = Viewport.HorizontalOffset;
                var viewTop = Viewport.VerticalOffset;
                var viewRight = viewLeft + vw;
                var viewBottom = viewTop + vh;
                if (!double.IsNaN(_lastRefreshLeft) &&
                    viewLeft >= _lastRefreshLeft && viewRight <= _lastRefreshRight &&
                    viewTop >= _lastRefreshTop && viewBottom <= _lastRefreshBottom)
                {
                    return;
                }
            }
            _lastRefreshLeft = left; _lastRefreshTop = top; _lastRefreshRight = right; _lastRefreshBottom = bottom;

            var candidates = Graph.Nodes.Where(n =>
                n.EditorX + 260 >= left && n.EditorX <= right &&
                n.EditorY + 120 >= top && n.EditorY <= bottom);

            var centerX = (left + right) * 0.5;
            var centerY = (top + bottom) * 0.5;
            var visible = candidates
                .OrderBy(n => { var dx = n.EditorX - centerX; var dy = n.EditorY - centerY; return dx * dx + dy * dy; })
                .Take(MaxRenderedNodes)
                .ToDictionary(n => n.Id, StringComparer.Ordinal);

            foreach (var id in _nodeViews.Keys.ToArray())
            {
                if (!visible.ContainsKey(id))
                {
                    Canvas.Children.Remove(_nodeViews[id]);
                    _nodeViews.Remove(id);
                }
            }

            foreach (var node in visible.Values)
            {
                if (!_nodeViews.ContainsKey(node.Id))
                    AddNode(node);
                else
                {
                    Canvas.SetLeft(_nodeViews[node.Id], node.EditorX);
                    Canvas.SetTop(_nodeViews[node.Id], node.EditorY);
                }
            }

            var activeEdges = Graph.Edges.Where(e => visible.ContainsKey(e.SourceNodeId) && visible.ContainsKey(e.TargetNodeId))
                .Take(MaxRenderedEdges)
                .ToDictionary(e => e.Id, StringComparer.Ordinal);

            foreach (var id in _edgeViews.Keys.ToArray())
            {
                if (!activeEdges.ContainsKey(id))
                {
                    Canvas.Children.Remove(_edgeViews[id]);
                    if (_arrowViews.TryGetValue(id, out var oldArrow)) Canvas.Children.Remove(oldArrow);
                    _edgeViews.Remove(id);
                    _arrowViews.Remove(id);
                }
            }

            foreach (var edge in activeEdges.Values)
            {
                if (!_nodeById.TryGetValue(edge.SourceNodeId, out var source) || !_nodeById.TryGetValue(edge.TargetNodeId, out var target)) continue;
                if (!_edgeViews.TryGetValue(edge.Id, out var line))
                {
                    var edgeId = edge.Id;
                    line = new Line { Stroke = SteelGreyBrush, Opacity = 0.8, Tag = edge };
                    
                    line.ContextMenuOpening += (_, _) =>
                        line.ContextMenu = CreateEdgeMenu(_edgeById.TryGetValue(edgeId, out var liveEdge) ? liveEdge : edge);
                    _edgeViews[edge.Id] = line;
                    Canvas.Children.Add(line);
                    var arrow = CreateArrow(source.EditorX + 125, source.EditorY + 40, target.EditorX + 125, target.EditorY + 40, line.Stroke);
                    _arrowViews[edge.Id] = arrow;
                    Canvas.Children.Add(arrow);
                }
                line.StrokeThickness = edge.Kind == MacroGraphEdgeKind.Next ? 1.2 : 2.0;
                line.X1 = source.EditorX + 125; line.Y1 = source.EditorY + 40;
                line.X2 = target.EditorX + 125; line.Y2 = target.EditorY + 40;
                if (_arrowViews.TryGetValue(edge.Id, out var arrowView)) UpdateArrow(arrowView, line.X1, line.Y1, line.X2, line.Y2);
            }
        }
        finally
        {
            _virtualizing = false;
        }
    }

    private void OnViewportChanged(object sender, ScrollChangedEventArgs e) => RefreshVirtualizedVisuals(false);
    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RefreshVirtualizedVisuals(false);

    private ContextMenu CreateEdgeMenu(MacroGraphEdge edge)
    {
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<MacroGraphEdgeKind>())
        {
            var item = new MenuItem
            {
                Header = kind.ToString(),
                IsChecked = kind == edge.Kind
            };
            var captured = kind;
            item.Click += (_, _) => EdgeKindChanged?.Invoke(
                this,
                new EdgeKindChangedEventArgs { EdgeId = edge.Id, Kind = captured });
            menu.Items.Add(item);
        }
        return menu;
    }

    private void AddNode(MacroGraphNode node)
    {
        var title = node.IsMousePath ? "MOUSE PATH" : node.DisplayType;
        var detail = node.IsMousePath && node.PathPoints is { Count: > 1 }
            ? $"{node.PathMode} · {node.PathPoints.Count} точек · {node.PathPoints[^1].OffsetMs} ms"
            : $"Событий: {node.SourceEvents.Count}";

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = FogWhiteBrush,
            FontWeight = FontWeights.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Foreground = SteelGreyBrush,
            Margin = new Thickness(0, 5, 0, 0)
        });

        if (node.IsMousePath && node.PathPoints is { Count: > 1 })
        {
            var points = BuildPreview(node.PathPoints);
            stack.Children.Add(new Polyline
            {
                Points = points,
                Stroke = SignalBlueBrush,
                StrokeThickness = 2,
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        var border = new Border
        {
            Width = 250,
            MinHeight = node.IsMousePath ? 110 : 72,
            Padding = new Thickness(10),
            Background = SlatePanelBrush,
            BorderBrush = node.IsMousePath
                ? SignalBlueBrush
                : SteelGreyBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = stack,
            Tag = node.Id
        };

        border.MouseLeftButtonDown += OnNodeDown;
        border.MouseLeftButtonDown += OnNodeDoubleClickCandidate;
        border.MouseMove += OnNodeMove;
        border.MouseLeftButtonUp += OnNodeUp;
        _nodeViews[node.Id] = border;
        Canvas.SetLeft(border, node.EditorX);
        Canvas.SetTop(border, node.EditorY);
        Canvas.Children.Add(border);
    }

    private void OnNodeDoubleClickCandidate(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is Border border && border.Tag is string id)
        {
            NodeEditRequested?.Invoke(this,new NodeEditRequestedEventArgs{NodeId=id});
            e.Handled=true;
        }
    }

    private void OnNodeDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string id) return;
        NodeSelectionChanged?.Invoke(this, new NodeSelectionChangedEventArgs { NodeId = id });
        _draggingId = id;
        _dragOffset = e.GetPosition(border);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void OnNodeMove(object sender, MouseEventArgs e)
    {
        if (_draggingId is null || sender is not Border border || !border.IsMouseCaptured) return;
        var point = e.GetPosition(Canvas);
        var x = Math.Max(0, point.X - _dragOffset.X);
        var y = Math.Max(0, point.Y - _dragOffset.Y);
        if (SnapToGrid && GridSize > 1)
        {
            x = Math.Round(x / GridSize) * GridSize;
            y = Math.Round(y / GridSize) * GridSize;
        }
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        if (Graph is not null)
        {
            var node = _nodeById.TryGetValue(_draggingId, out var cachedNode)
                ? cachedNode
                : null;
            if (node is not null)
            {
                node.EditorX = x;
                node.EditorY = y;
            }
        }
        RedrawEdges();
        e.Handled = true;
    }

    private void OnNodeUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border) border.ReleaseMouseCapture();
        if (_draggingId is { } id && Graph is not null)
        {
            var node = Graph.Nodes.FirstOrDefault(n => n.Id == id);
            if (node is not null)
                NodePositionChanged?.Invoke(this, new NodePositionChangedEventArgs
                {
                    NodeId = id, X = node.EditorX, Y = node.EditorY
                });
        }
        _draggingId = null;
        e.Handled = true;
    }

    private void RedrawEdges()
    {
        if (Graph is null || _draggingId is null) return;
        if (!_edgesByNodeId.TryGetValue(_draggingId, out var edges)) return;
        foreach (var edge in edges)
        {
            if (!_edgeViews.TryGetValue(edge.Id, out var line) ||
                !_nodeById.TryGetValue(edge.SourceNodeId, out var source) ||
                !_nodeById.TryGetValue(edge.TargetNodeId, out var target)) continue;
            line.X1 = source.EditorX + 125; line.Y1 = source.EditorY + 40;
            line.X2 = target.EditorX + 125; line.Y2 = target.EditorY + 40;
            if (_arrowViews.TryGetValue(edge.Id, out var arrow))
                UpdateArrow(arrow, line.X1, line.Y1, line.X2, line.Y2);
        }
    }

    private static Polygon CreateArrow(double x1, double y1, double x2, double y2, Brush stroke)
    {
        var polygon = new Polygon { Fill = stroke, Stroke = stroke };
        UpdateArrow(polygon, x1, y1, x2, y2);
        return polygon;
    }

    private static void UpdateArrow(Polygon polygon, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1; var dy = y2 - y1;
        var len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        dx /= len; dy /= len;
        var px = -dy; var py = dx;
        var tip = new Point(x2, y2);
        var left = new Point(x2 - dx * 10 + px * 4, y2 - dy * 10 + py * 4);
        var right = new Point(x2 - dx * 10 - px * 4, y2 - dy * 10 - py * 4);
        polygon.Points = new PointCollection { tip, left, right };
    }

    private static PointCollection BuildPreview(IReadOnlyList<MousePathPoint> points)
    {
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double dx = Math.Max(maxX - minX, 1e-9), dy = Math.Max(maxY - minY, 1e-9);
        var result = new PointCollection();
        foreach (var p in points)
            result.Add(new Point((p.X - minX) / dx * 220 + 2, 42 - (p.Y - minY) / dy * 36));
        return result;
    }
}
