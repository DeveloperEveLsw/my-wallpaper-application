#if WINDOWS
using System.Windows.Media.Imaging;

namespace Wallpaper.Infrastructure.Windows.Visuals;

public sealed record FileVisualResult(
    BitmapSource Source,
    FileVisualKind Kind,
    FileVisualPresentation Presentation,
    int SourcePixelWidth,
    int SourcePixelHeight,
    string? DisplayName = null);

public enum FileVisualKind
{
    ShellIcon,
    Thumbnail,
}
#endif
