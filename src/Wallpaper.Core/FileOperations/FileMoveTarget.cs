namespace Wallpaper.Core.FileOperations;

public sealed record FileMoveDestination(
    string RootPath,
    string? RelativeFolderPath);

public sealed record ValidatedFileMoveDestination(
    FileMoveDestination Destination,
    string RootPath,
    string AbsolutePath);

public sealed record FileMovePreparation(
    FileCommandTarget Source,
    FileMoveDestination Destination,
    string RequestedName,
    string ProposedName,
    bool HasNameCollision);
