#if WINDOWS
using Wallpaper.Core.Models;

namespace Wallpaper.Infrastructure.Windows.Visuals;

public interface IFileVisualService
{
    Task<FileVisualResult?> LoadShellIconAsync(
        DesktopFile file,
        string absolutePath,
        int targetPixelWidth,
        CancellationToken cancellationToken = default);

    Task<FileVisualResult?> LoadThumbnailAsync(
        DesktopFile file,
        string absolutePath,
        int targetPixelWidth,
        CancellationToken cancellationToken = default);
}
#endif
