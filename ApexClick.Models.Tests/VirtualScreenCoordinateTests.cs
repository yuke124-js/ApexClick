using ApexClick.Models;

namespace ApexClick.Models.Tests;

public sealed class VirtualScreenCoordinateTests
{
    [Fact]
    public void PointOnPrimaryRemainsInsideVirtualDesktop()
    {
        var virtualScreen = new VirtualScreenBounds(-1920, 0, 3840, 1080);
        var point = ScreenCoordinateMath.ToVirtualNormalizedFromPrimaryNormalized(
            0.5, 0.5, 1920, 1080, virtualScreen);

        Assert.Equal(0.5, point.X, 6);
        Assert.Equal(0.5, point.Y, 6);
    }

    [Fact]
    public void VirtualNormalizationKeepsLeftMonitorInsideVirtualBounds()
    {
        var virtualScreen = new VirtualScreenBounds(-1920, 0, 3840, 1080);
        var point = virtualScreen.ToNormalized(-960, 540);

        Assert.Equal(0.25, point.X, 6);
        Assert.Equal(0.5, point.Y, 6);
    }
}
