namespace Wallpaper.Core.FileOperations;

public sealed class FileCommandException : IOException
{
    public FileCommandException(FileCommandError error, string message)
        : base(message)
    {
        Error = error;
    }

    public FileCommandException(FileCommandError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    public FileCommandError Error { get; }
}

public enum FileCommandError
{
    InvalidTarget,
    TargetMissing,
    UnsupportedTarget,
    InvalidName,
    NameCollision,
    NoChange,
    OpenFailed,
    ExplorerFailed,
    RenameFailed,
    RecycleFailed,
    RecycleCancelled,
}
