using System.Drawing;
using System.Drawing.Imaging;
using ApexClick.Services.Vision;
using Xunit;

namespace ApexClick.Services.Vision.Tests;

public class ImageAnalysisServiceTests
{
    [Fact]
    public async Task FindTemplateAsync_FindsEmbeddedTemplate_WithNormalizedLocation()
    {
        byte[] source = CreatePng(100, 80, Color.White, new Rectangle(25, 16, 20, 16), Color.Black);
        string templatePath = WriteTempPng(CreatePng(20, 16, Color.Black));

        try
        {
            var (found, confidence, location) = await new ImageAnalysisService()
                .FindTemplateAsync(source, templatePath, confidenceThreshold: 0.99);

            Assert.True(found);
            Assert.InRange(confidence, 0.99, 1.0);
            Assert.NotNull(location);
            
            Assert.Equal(0.25, location!.Value.X, precision: 3);
            Assert.Equal(0.20, location.Value.Y, precision: 3);
            Assert.Equal(0.20, location.Value.Width, precision: 3);
            Assert.Equal(0.20, location.Value.Height, precision: 3);
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    [Fact]
    public async Task FindTemplateAsync_BelowThreshold_ReturnsNotFound()
    {
        byte[] source = CreatePng(50, 50, Color.White);
        string templatePath = WriteTempPng(CreatePng(10, 10, Color.Black));

        try
        {
            var (found, _, location) = await new ImageAnalysisService()
                .FindTemplateAsync(source, templatePath, confidenceThreshold: 0.9);

            Assert.False(found);
            Assert.Null(location);
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    [Fact]
    public async Task FindTemplateAsync_TemplateLargerThanSource_ReturnsNotFoundWithoutThrowing()
    {
        byte[] source = CreatePng(10, 10, Color.White);
        string templatePath = WriteTempPng(CreatePng(20, 20, Color.Black));

        try
        {
            var (found, confidence, location) = await new ImageAnalysisService()
                .FindTemplateAsync(source, templatePath, confidenceThreshold: 0.8);

            Assert.False(found);
            Assert.Equal(0.0, confidence);
            Assert.Null(location);
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    [Fact]
    public void DetectWindowHang_UnchangedFrame_ReturnsTrueOnlyAfterThreshold()
    {
        var service = new ImageAnalysisService();
        byte[] frame = CreatePng(10, 10, Color.Gray);

        Assert.False(service.DetectWindowHang(frame, TimeSpan.FromMilliseconds(50)));
        Assert.False(service.DetectWindowHang(frame, TimeSpan.FromSeconds(10)));

        Thread.Sleep(60);
        Assert.True(service.DetectWindowHang(frame, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void DetectWindowHang_ChangedFrame_ResetsTimer()
    {
        var service = new ImageAnalysisService();
        Assert.False(service.DetectWindowHang(CreatePng(10, 10, Color.Gray), TimeSpan.Zero));
        Assert.False(service.DetectWindowHang(CreatePng(10, 10, Color.Red), TimeSpan.Zero));
    }

    private static byte[] CreatePng(int width, int height, Color background, Rectangle? rectangle = null, Color? rectangleColor = null)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(background);
            if (rectangle is { } rect)
            {
                using var brush = new SolidBrush(rectangleColor ?? Color.Black);
                graphics.FillRectangle(brush, rect);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static string WriteTempPng(byte[] png)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        File.WriteAllBytes(path, png);
        return path;
    }
}
