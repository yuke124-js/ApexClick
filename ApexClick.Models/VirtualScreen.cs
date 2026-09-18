namespace ApexClick.Models;

public readonly record struct VirtualScreenBounds(int Left, int Top, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;

    public (double X, double Y) ToNormalized(double primaryPixelX, double primaryPixelY)
    {
        if (!IsValid) throw new InvalidOperationException("Некорректные границы virtual screen.");
        return ((primaryPixelX - Left) / Width, (primaryPixelY - Top) / Height);
    }

    public (double X, double Y) ToVirtualPixels(double normalizedX, double normalizedY) =>
        (Left + normalizedX * Width, Top + normalizedY * Height);
}
