#if WINDOWS
using System.Windows.Media;
using Wallpaper.Core.Models;

namespace Wallpaper.Infrastructure.Windows.Visuals;

public interface IFileVisualService
{
    Task<ImageSource?> LoadShellIconAsync(
        DesktopFile file,
        string absolutePath,
        CancellationToken cancellationToken = default);

    Task<ImageSource?> LoadThumbnailAsync(
        DesktopFile file,
        string absolutePath,
        CancellationToken cancellationToken = default);
}
#endif
