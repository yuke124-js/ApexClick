using System.Windows;
using ApexClick.ViewModels;

namespace ApexClick.Views;

public partial class PlaybackHudWindow : Window
{
    private const double MarginFromEdge = 24;

    public PlaybackHudWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - MarginFromEdge;
        Top = workArea.Bottom - ActualHeight - MarginFromEdge;
    }
}
