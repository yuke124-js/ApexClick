using ApexClick.Models;

namespace ApexClick.Models.Tests;

public class ScreenCoordinateMathTests
{
    [Theory]
    [InlineData(960, 540, 1920, 1080, 0.5, 0.5)]
    [InlineData(0, 0, 1920, 1080, 0.0, 0.0)]
    [InlineData(1920, 1080, 1920, 1080, 1.0, 1.0)]
    public void ToNormalized_ComputesFraction(int px, int py, int w, int h, double expX, double expY)
    {
        var (x, y) = ScreenCoordinateMath.ToNormalized(px, py, w, h);
        Assert.Equal(expX, x, precision: 6);
        Assert.Equal(expY, y, precision: 6);
    }

    [Fact]
    public void ToAbsolutePixels_IsInverseOfToNormalized()
    {
        var (nx, ny) = ScreenCoordinateMath.ToNormalized(1200, 300, 1920, 1080);
        var (px, py) = ScreenCoordinateMath.ToAbsolutePixels(nx, ny, 1920, 1080);
        Assert.Equal(1200, px);
        Assert.Equal(300, py);
    }

    [Fact]
    public void ToSendInputAbsoluteRange_MapsFullRangeAndClamps()
    {
        Assert.Equal((0, 0), ScreenCoordinateMath.ToSendInputAbsoluteRange(0.0, 0.0));
        Assert.Equal((65535, 65535), ScreenCoordinateMath.ToSendInputAbsoluteRange(1.0, 1.0));
        Assert.Equal((65535, 0), ScreenCoordinateMath.ToSendInputAbsoluteRange(1.5, -0.5));
    }

    [Fact]
    public void ToNormalized_RejectsNonPositiveScreenSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenCoordinateMath.ToNormalized(1, 1, 0, 1080));
    }
}
