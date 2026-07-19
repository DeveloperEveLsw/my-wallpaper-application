namespace Wallpaper.Core.Models;

public sealed record DesktopSnapshot(
    string RootPath,
    string RootName,
    IReadOnlyList<DesktopFolder> Folders,
    IReadOnlyList<DesktopFile> RootFiles,
    IReadOnlyList<ScanWarning> Warnings,
    DateTimeOffset CapturedAtUtc);
