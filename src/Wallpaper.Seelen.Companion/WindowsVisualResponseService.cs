using System.IO;
using System.Windows.Media.Imaging;
using Wallpaper.Infrastructure.Windows.Visuals;

namespace Wallpaper.Seelen.Companion;

internal sealed class WindowsVisualResponseService(DesktopProjectionService projection)
{
    private const int IconPixels = 64;
    private const int ThumbnailPixels = 320;
    private readonly WindowsFileVisualService _visuals = new();

    public async Task<VisualResponse?> LoadAsync(
        string kind,
        string id,
        CancellationToken cancellationToken)
    {
        if (!projection.TryGetFile(id, out var target) || target is null)
        {
            return null;
        }

        try
        {
            var attributes = File.GetAttributes(target.AbsolutePath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return null;
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            return null;
        }

        FileVisualResult? visual;
        if (string.Equals(kind, "thumbnail", StringComparison.Ordinal))
        {
            visual = await _visuals.LoadThumbnailAsync(
                ToDesktopFile(target),
                target.AbsolutePath,
                ThumbnailPixels,
                cancellationToken);
        }
        else if (string.Equals(kind, "icon", StringComparison.Ordinal))
        {
            visual = await _visuals.LoadShellIconAsync(
                ToDesktopFile(target),
                target.AbsolutePath,
                IconPixels,
                cancellationToken);
        }
        else
        {
            return null;
        }

        if (visual is null)
        {
            return null;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(visual.Source));
        await using var stream = new MemoryStream();
        encoder.Save(stream);
        return new VisualResponse(
            stream.ToArray(),
            "image/png",
            visual.Presentation.ToString().ToLowerInvariant(),
            visual.DisplayName);
    }

    private static Wallpaper.Core.Models.DesktopFile ToDesktopFile(ProjectionFileTarget target) =>
        new(
            target.File.Id,
            target.File.Name,
            target.File.RelativePath,
            target.File.Extension,
            target.File.Length,
            target.File.LastWriteTimeUtc);
}

internal sealed record VisualResponse(
    byte[] Bytes,
    string ContentType,
    string Presentation,
    string? DisplayName);
