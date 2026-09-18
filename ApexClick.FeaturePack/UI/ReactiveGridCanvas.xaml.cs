using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ApexClick.FeaturePack.UI;

public partial class ReactiveGridCanvas : UserControl
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(ReactiveGridCanvas),
            new PropertyMetadata(true, OnActiveChanged));

    private bool _running;
    private Point _cursor;
    private Point _lastRenderedCursor;
    private long _lastRenderMs;
    private bool _hasRenderedCursor;
    private Brush? _gridBrush;
    private static readonly Brush FallbackBrush = CreateFallbackBrush();

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public ReactiveGridCanvas()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ReactiveGridCanvas canvas) return;
        if ((bool)e.NewValue) canvas.Start(); else canvas.Stop();
        canvas.InvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        Stop();
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSystemParametersChanged(sender, e)));
            return;
        }

        if (SystemParameters.ClientAreaAnimation && IsActive) Start();
        else Stop();
        InvalidateVisual();
    }

    private void Start()
    {
        if (_running || !IsActive || !SystemParameters.ClientAreaAnimation || !IsLoaded) return;
        _gridBrush = TryFindResource("SteelGreyBrush") as Brush ?? FallbackBrush;
        _running = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Stop()
    {
        if (!_running) return;
        CompositionTarget.Rendering -= OnRendering;
        _running = false;
        InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsActive || !SystemParameters.ClientAreaAnimation)
        {
            Stop();
            return;
        }

        long now = Environment.TickCount64;
        if (now - _lastRenderMs < 33)
            return;

        var cursor = Mouse.GetPosition(this);
        _cursor = cursor;

        if (_hasRenderedCursor &&
            Math.Abs(_cursor.X - _lastRenderedCursor.X) < 1 &&
            Math.Abs(_cursor.Y - _lastRenderedCursor.Y) < 1)
            return;

        _lastRenderMs = now;
        _lastRenderedCursor = _cursor;
        _hasRenderedCursor = true;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var brush = _gridBrush ?? TryFindResource("SteelGreyBrush") as Brush ?? FallbackBrush;
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);
        const double spacing = 20;
        const int maxColumns = 50;
        const int maxRows = 30;

        var columns = Math.Min(maxColumns, (int)(width / spacing) + 2);
        var rows = Math.Min(maxRows, (int)(height / spacing) + 2);
        var originX = width / 2d;
        var originY = height / 2d;

        var animated = IsActive && SystemParameters.ClientAreaAnimation;
        var radius = 90d;

        for (var row = -rows / 2; row <= rows / 2; row++)
        for (var col = -columns / 2; col <= columns / 2; col++)
        {
            var x = originX + col * spacing;
            var y = originY + row * spacing;
            var influence = animated
                ? Math.Clamp(1d - Distance(x, y, _cursor.X, _cursor.Y) / radius, 0d, 1d)
                : 0d;
            var size = 3d * (1d + influence * 1.5);

            dc.PushOpacity(animated ? 0.45 + 0.55 * influence : 0.45);
            dc.DrawRectangle(brush, null,
                new Rect(x - size / 2d, y - size / 2d, size, size));
            dc.Pop();
        }
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (VisualParent is null) Stop();
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Brush CreateFallbackBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(45, 139, 146, 163));
        brush.Freeze();
        return brush;
    }
}
