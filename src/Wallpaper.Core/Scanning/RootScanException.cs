namespace Wallpaper.Core.Scanning;

public sealed class RootScanException : Exception
{
    public RootScanException(RootScanError error, string message)
        : base(message)
    {
        Error = error;
    }

    public RootScanException(RootScanError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    public RootScanError Error { get; }
}

public enum RootScanError
{
    EmptyPath,
    PathNotFullyQualified,
    DirectoryNotFound,
    RootIsReparsePoint,
    AccessDenied,
    IoFailure,
}
