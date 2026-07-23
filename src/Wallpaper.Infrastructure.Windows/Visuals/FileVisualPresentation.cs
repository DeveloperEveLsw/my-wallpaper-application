namespace Wallpaper.Infrastructure.Windows.Visuals;

public enum FileVisualPresentation
{
    None,
    FullBleed,
    Contained,
}

internal readonly record struct AlphaCoverageAnalysis(
    FileVisualPresentation Presentation,
    int Left,
    int Top,
    int Width,
    int Height);

internal static class FileVisualAlphaAnalyzer
{
    private const byte VisibleAlphaThreshold = 8;
    // Rounded-square artwork keeps most pixels visible; circular or padded glyphs stay below this ratio.
    private const double FullBleedCoverageThreshold = 0.88;

    public static AlphaCoverageAnalysis Analyze(
        ReadOnlySpan<byte> bgraPixels,
        int pixelWidth,
        int pixelHeight,
        int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        if (stride < checked(pixelWidth * 4))
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        var requiredLength = checked(stride * pixelHeight);
        if (bgraPixels.Length < requiredLength)
        {
            throw new ArgumentException("The pixel buffer is smaller than the declared image size.", nameof(bgraPixels));
        }

        var left = pixelWidth;
        var top = pixelHeight;
        var right = -1;
        var bottom = -1;
        long visiblePixelCount = 0;

        for (var y = 0; y < pixelHeight; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < pixelWidth; x++)
            {
                if (bgraPixels[rowOffset + (x * 4) + 3] <= VisibleAlphaThreshold)
                {
                    continue;
                }

                visiblePixelCount++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (visiblePixelCount == 0)
        {
            return new AlphaCoverageAnalysis(
                FileVisualPresentation.Contained,
                0,
                0,
                pixelWidth,
                pixelHeight);
        }

        var totalPixelCount = (long)pixelWidth * pixelHeight;
        var presentation = visiblePixelCount / (double)totalPixelCount >= FullBleedCoverageThreshold
            ? FileVisualPresentation.FullBleed
            : FileVisualPresentation.Contained;

        return new AlphaCoverageAnalysis(
            presentation,
            left,
            top,
            right - left + 1,
            bottom - top + 1);
    }
}
