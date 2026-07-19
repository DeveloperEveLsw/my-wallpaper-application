namespace Wallpaper.Core.Models;

public sealed record DesktopFile(
    string Id,
    string Name,
    string RelativePath,
    string Extension,
    long Length,
    DateTimeOffset LastWriteTimeUtc);
