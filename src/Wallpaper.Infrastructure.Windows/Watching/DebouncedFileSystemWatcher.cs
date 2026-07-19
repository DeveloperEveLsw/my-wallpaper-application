using System.IO;

namespace Wallpaper.Infrastructure.Windows.Watching;

public sealed class DebouncedFileSystemWatcher : IRootChangeWatcher
{
    private const int WatcherBufferSize = 64 * 1024;

    private readonly object _gate = new();
    private readonly TimeSpan _debounceInterval;
    private FileSystemWatcher? _contentWatcher;
    private FileSystemWatcher? _parentWatcher;
    private Timer? _debounceTimer;
    private RootChangeReason _pendingReason;
    private string? _rootPath;
    private long _generation;
    private bool _disposed;

    public DebouncedFileSystemWatcher(TimeSpan? debounceInterval = null)
    {
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(450);
        if (_debounceInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceInterval));
        }
    }

    public event EventHandler<RootChangedEventArgs>? Changed;

    public RootWatchStatus Watch(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();

            _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var generation = ++_generation;
            var warnings = new List<string>();
            var contentWatching = TryCreateContentWatcher(generation, warnings);
            var parentWatching = TryCreateParentWatcher(generation, warnings);
            return new RootWatchStatus(
                contentWatching,
                parentWatching,
                warnings.Count == 0 ? null : string.Join(" ", warnings));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                StopCore();
                _rootPath = null;
                _generation++;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCore();
            _rootPath = null;
            _generation++;
        }
    }

    private bool TryCreateContentWatcher(long generation, ICollection<string> warnings)
    {
        if (_rootPath is null || !Directory.Exists(_rootPath))
        {
            return false;
        }

        try
        {
            var watcher = new FileSystemWatcher(_rootPath)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = WatcherBufferSize,
                NotifyFilter = NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.Attributes |
                    NotifyFilters.CreationTime |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size,
            };

            watcher.Changed += (_, args) => OnContentEvent(generation, args.FullPath);
            watcher.Created += (_, args) => OnContentEvent(generation, args.FullPath);
            watcher.Deleted += (_, args) => OnContentEvent(generation, args.FullPath);
            watcher.Renamed += (_, args) => OnRenamedEvent(generation, args);
            watcher.Error += (_, _) => Schedule(generation, RootChangeReason.WatcherError);
            watcher.EnableRaisingEvents = true;
            _contentWatcher = watcher;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            warnings.Add("폴더 변경 감시를 시작할 수 없습니다.");
            return false;
        }
    }

    private bool TryCreateParentWatcher(long generation, ICollection<string> warnings)
    {
        if (_rootPath is null)
        {
            return false;
        }

        var parentPath = Directory.GetParent(_rootPath)?.FullName;
        var rootName = Path.GetFileName(_rootPath);
        if (string.IsNullOrWhiteSpace(parentPath) ||
            string.IsNullOrWhiteSpace(rootName) ||
            !Directory.Exists(parentPath))
        {
            return false;
        }

        try
        {
            var watcher = new FileSystemWatcher(parentPath, rootName)
            {
                IncludeSubdirectories = false,
                InternalBufferSize = WatcherBufferSize,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.Attributes,
            };

            watcher.Created += (_, _) => Schedule(generation, RootChangeReason.RootAvailabilityChanged);
            watcher.Deleted += (_, _) => Schedule(generation, RootChangeReason.RootAvailabilityChanged);
            watcher.Renamed += (_, _) => Schedule(generation, RootChangeReason.RootAvailabilityChanged);
            watcher.Error += (_, _) => Schedule(generation, RootChangeReason.WatcherError);
            watcher.EnableRaisingEvents = true;
            _parentWatcher = watcher;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            warnings.Add("루트 삭제 감시를 시작할 수 없습니다.");
            return false;
        }
    }

    private void OnRenamedEvent(long generation, RenamedEventArgs args)
    {
        if (IsProjectedPath(args.FullPath) || IsProjectedPath(args.OldFullPath))
        {
            Schedule(generation, RootChangeReason.ContentChanged);
        }
    }

    private void OnContentEvent(long generation, string fullPath)
    {
        if (IsProjectedPath(fullPath))
        {
            Schedule(generation, RootChangeReason.ContentChanged);
        }
    }

    private bool IsProjectedPath(string fullPath)
    {
        string? rootPath;
        lock (_gate)
        {
            rootPath = _rootPath;
        }

        if (rootPath is null)
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        if (relativePath == "." ||
            relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        var depth = 1;
        foreach (var character in relativePath)
        {
            if (character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar)
            {
                depth++;
            }
        }

        return depth <= 2;
    }

    private void Schedule(long generation, RootChangeReason reason)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }

            if (reason > _pendingReason)
            {
                _pendingReason = reason;
            }

            _debounceTimer ??= new Timer(_ => OnDebounceElapsed(generation));
            _debounceTimer.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(long generation)
    {
        RootChangeReason reason;
        lock (_gate)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }

            reason = _pendingReason;
            _pendingReason = RootChangeReason.ContentChanged;
        }

        Changed?.Invoke(this, new RootChangedEventArgs(reason));
    }

    private void StopCore()
    {
        _contentWatcher?.Dispose();
        _contentWatcher = null;
        _parentWatcher?.Dispose();
        _parentWatcher = null;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _pendingReason = RootChangeReason.ContentChanged;
    }
}
