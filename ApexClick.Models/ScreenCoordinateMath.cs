namespace ApexClick.Models;

public static class ScreenCoordinateMath
{
    public static (double X, double Y) ToNormalized(int pixelX, int pixelY, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        return ((double)pixelX / screenWidth, (double)pixelY / screenHeight);
    }

    public static (double X, double Y) ToVirtualNormalizedFromPrimaryNormalized(
        double primaryNormalizedX,
        double primaryNormalizedY,
        int primaryWidth,
        int primaryHeight,
        VirtualScreenBounds virtualScreen)
    {
        var primaryX = primaryNormalizedX * primaryWidth;
        var primaryY = primaryNormalizedY * primaryHeight;
        return virtualScreen.ToNormalized(primaryX, primaryY);
    }

    public static (double X, double Y) FromVirtualNormalizedToPrimaryNormalized(
        double virtualNormalizedX,
        double virtualNormalizedY,
        int primaryWidth,
        int primaryHeight,
        VirtualScreenBounds virtualScreen)
    {
        var (x, y) = virtualScreen.ToVirtualPixels(virtualNormalizedX, virtualNormalizedY);
        return (x / primaryWidth, y / primaryHeight);
    }

    public static (int X, int Y) ToAbsolutePixels(double normalizedX, double normalizedY, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        return (
            (int)Math.Round(normalizedX * screenWidth, MidpointRounding.AwayFromZero),
            (int)Math.Round(normalizedY * screenHeight, MidpointRounding.AwayFromZero));
    }

    public static (int X, int Y) ToSendInputAbsoluteRange(double normalizedX, double normalizedY)
    {
        const int max = 65535;
        return (
            (int)Math.Round(Math.Clamp(normalizedX, 0.0, 1.0) * max, MidpointRounding.AwayFromZero),
            (int)Math.Round(Math.Clamp(normalizedY, 0.0, 1.0) * max, MidpointRounding.AwayFromZero));
    }

    public static (int X, int Y) ToSendInputVirtualDesktopAbsoluteRange(double normalizedX,double normalizedY)
    {
        const int max=65535;
        return ((int)Math.Round(Math.Clamp(normalizedX,0,1)*max,MidpointRounding.AwayFromZero),(int)Math.Round(Math.Clamp(normalizedY,0,1)*max,MidpointRounding.AwayFromZero));
    }
}
