namespace Wallpaper.Seelen;

public sealed record ProjectionSnapshot(
    long Revision,
    string State,
    string RootPath,
    string RootName,
    bool RootConfigured,
    IReadOnlyList<ProjectionFolder> Folders,
    ProjectionFolder LooseFiles,
    IReadOnlyList<ProjectionWarning> Warnings,
    string? Message,
    DateTimeOffset CapturedAtUtc);

public sealed record ProjectionFolder(
    string Id,
    string Name,
    string RelativePath,
    bool IsLooseFiles,
    IReadOnlyList<ProjectionFile> Files);

public sealed record ProjectionFile(
    string Id,
    string Name,
    string RelativePath,
    string Extension,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string IconPath,
    string? ThumbnailPath);

public sealed record ProjectionWarning(string RelativePath, string Code);

public sealed record ProjectionFileTarget(
    string Id,
    string AbsolutePath,
    ProjectionFile File);

public sealed record ProjectionStatus(
    bool ContentWatching,
    bool ParentWatching,
    string? Warning);
