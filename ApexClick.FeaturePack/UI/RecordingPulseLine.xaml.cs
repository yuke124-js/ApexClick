using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ApexClick.FeaturePack.UI;

public partial class RecordingPulseLine : UserControl
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(RecordingPulseLine),
            new PropertyMetadata(false, OnVisualStateChanged));

    public static readonly DependencyProperty PulseTokenProperty =
        DependencyProperty.Register(nameof(PulseToken), typeof(long), typeof(RecordingPulseLine),
            new PropertyMetadata(0L, OnPulseTokenChanged));

    private readonly Stopwatch _throttle = Stopwatch.StartNew();
    private readonly DoubleAnimation _pulseAnimation;

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public long PulseToken { get => (long)GetValue(PulseTokenProperty); set => SetValue(PulseTokenProperty, value); }

    public RecordingPulseLine()
    {
        InitializeComponent();
        _pulseAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(130),
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        _throttle.Restart();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        UpdateVisualState();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        PulsePath.BeginAnimation(UIElement.OpacityProperty, null);
        PulsePath.Opacity = 0;
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSystemParametersChanged(sender, e)));
            return;
        }
        UpdateVisualState();
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RecordingPulseLine line) line.UpdateVisualState();
    }

    private static void OnPulseTokenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RecordingPulseLine line && line.IsActive && SystemParameters.ClientAreaAnimation)
            line.Pulse();
    }

    private void UpdateVisualState()
    {
        PulsePath.BeginAnimation(UIElement.OpacityProperty, null);
        PulsePath.Opacity = 0;
        if (IsActive && SystemParameters.ClientAreaAnimation) Pulse();
    }

    private void Pulse()
    {
        if (!IsLoaded || !IsActive || !SystemParameters.ClientAreaAnimation) return;
        if (_throttle.ElapsedMilliseconds < 40) return;
        _throttle.Restart();
        PulsePath.BeginAnimation(UIElement.OpacityProperty, _pulseAnimation, HandoffBehavior.SnapshotAndReplace);
    }
}
