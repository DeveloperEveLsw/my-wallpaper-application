namespace Wallpaper.Infrastructure.Windows.Watching;

public interface IRootChangeWatcher : IDisposable
{
    event EventHandler<RootChangedEventArgs>? Changed;

    RootWatchStatus Watch(string rootPath);

    void Stop();
}

public sealed class RootChangedEventArgs(RootChangeReason reason) : EventArgs
{
    public RootChangeReason Reason { get; } = reason;
}

public enum RootChangeReason
{
    ContentChanged,
    RootAvailabilityChanged,
    WatcherError,
}

public sealed record RootWatchStatus(
    bool IsContentWatching,
    bool IsParentWatching,
    string? Warning)
{
    public bool IsWatching => IsContentWatching || IsParentWatching;
}
