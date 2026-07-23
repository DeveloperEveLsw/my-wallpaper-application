using Wallpaper.Infrastructure.Windows.Visuals;

namespace Wallpaper.Infrastructure.Windows.Tests;

public sealed class FileVisualAlphaAnalyzerTests
{
    [Fact]
    public void Analyze_ClassifiesOpaqueSurfaceAsFullBleed()
    {
        var pixels = CreatePixels(8, 8, (_, _) => byte.MaxValue);

        var result = FileVisualAlphaAnalyzer.Analyze(pixels, 8, 8, 8 * 4);

        Assert.Equal(FileVisualPresentation.FullBleed, result.Presentation);
        Assert.Equal((0, 0, 8, 8), (result.Left, result.Top, result.Width, result.Height));
    }

    [Fact]
    public void Analyze_AllowsTransparentRoundedCornersForFullBleedSurface()
    {
        var pixels = CreatePixels(
            16,
            16,
            (x, y) => x < 2 && y < 2 || x >= 14 && y < 2 || x < 2 && y >= 14 || x >= 14 && y >= 14
                ? (byte)0
                : byte.MaxValue);

        var result = FileVisualAlphaAnalyzer.Analyze(pixels, 16, 16, 16 * 4);

        Assert.Equal(FileVisualPresentation.FullBleed, result.Presentation);
    }

    [Fact]
    public void Analyze_ClassifiesTransparentPaddedGlyphAsContainedAndFindsContentBounds()
    {
        var pixels = CreatePixels(
            10,
            10,
            (x, y) => x is >= 3 and <= 6 && y is >= 2 and <= 7
                ? byte.MaxValue
                : (byte)0);

        var result = FileVisualAlphaAnalyzer.Analyze(pixels, 10, 10, 10 * 4);

        Assert.Equal(FileVisualPresentation.Contained, result.Presentation);
        Assert.Equal((3, 2, 4, 6), (result.Left, result.Top, result.Width, result.Height));
    }

    [Fact]
    public void Analyze_IgnoresNearTransparentEdgeNoise()
    {
        var pixels = CreatePixels(
            10,
            10,
            (x, y) => x is >= 3 and <= 6 && y is >= 3 and <= 6
                ? byte.MaxValue
                : (byte)4);

        var result = FileVisualAlphaAnalyzer.Analyze(pixels, 10, 10, 10 * 4);

        Assert.Equal(FileVisualPresentation.Contained, result.Presentation);
        Assert.Equal((3, 3, 4, 4), (result.Left, result.Top, result.Width, result.Height));
    }

    private static byte[] CreatePixels(
        int width,
        int height,
        Func<int, int, byte> getAlpha)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[((y * width) + x) * 4 + 3] = getAlpha(x, y);
            }
        }

        return pixels;
    }
}
