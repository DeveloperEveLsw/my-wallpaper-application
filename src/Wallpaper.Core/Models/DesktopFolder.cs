namespace Wallpaper.Core.Models;

public sealed record DesktopFolder(
    string Id,
    string Name,
    string RelativePath,
    IReadOnlyList<DesktopFile> Files);
