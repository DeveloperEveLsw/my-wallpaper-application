using Wallpaper.Core.Models;
using Wallpaper.Core.Scanning;
using Wallpaper.Core.Sorting;
using Wallpaper.Infrastructure.Windows.Settings;
using Wallpaper.Infrastructure.Windows.Watching;

namespace Wallpaper.Seelen;

public sealed class DesktopProjectionService : IAsyncDisposable
{
    private static readonly HashSet<string> ThumbnailExtensions = new(
        [".bmp", ".gif", ".jfif", ".jpe", ".jpeg", ".jpg", ".png", ".webp"],
        StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();
    private readonly IDesktopScanner _scanner;
    private readonly IRootChangeWatcher _watcher;
    private readonly IAppSettingsStore _settingsStore;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly string _defaultRootPath;
    private AppSettings _settings = AppSettings.Default;
    private Dictionary<string, ProjectionFileTarget> _files = new(StringComparer.OrdinalIgnoreCase);
    private ProjectionSnapshot? _current;
    private long _revision;
    private bool _disposed;

    public DesktopProjectionService(
        string defaultRootPath,
        IDesktopScanner? scanner = null,
        IRootChangeWatcher? watcher = null,
        IAppSettingsStore? settingsStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultRootPath);
        _defaultRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(defaultRootPath));
        _scanner = scanner ?? new ShallowDesktopScanner();
        _watcher = watcher ?? new DebouncedFileSystemWatcher();
        _settingsStore = settingsStore ?? new JsonAppSettingsStore();
        _watcher.Changed += OnRootChanged;
    }

    public event EventHandler<ProjectionSnapshot>? SnapshotChanged;

    public ProjectionSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current ?? CreateLoadingSnapshot();
            }
        }
    }

    public ProjectionStatus WatchStatus { get; private set; } = new(false, false, null);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var loaded = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        _settings = loaded.Settings;
        if (loaded.WasCorrupted)
        {
            _settings = AppSettings.Default;
            await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
        }

        await RefreshAsync(
            loaded.WasCorrupted ? "손상된 설정을 기본값으로 복구했습니다." : null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(
        string? statusMessage = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ProjectionSnapshot snapshot;
            Dictionary<string, ProjectionFileTarget> files;
            try
            {
                var rootPath = ResolveRootPath();
                var source = await Task.Run(
                    () => _scanner.Scan(rootPath),
                    cancellationToken).ConfigureAwait(false);
                (snapshot, files) = MapSnapshot(source, statusMessage);
                var watch = _watcher.Watch(rootPath);
                WatchStatus = new ProjectionStatus(
                    watch.IsContentWatching,
                    watch.IsParentWatching,
                    watch.Warning);
            }
            catch (RootScanException exception)
            {
                var rootPath = ResolveRootPath();
                var watch = _watcher.Watch(rootPath);
                WatchStatus = new ProjectionStatus(
                    watch.IsContentWatching,
                    watch.IsParentWatching,
                    watch.Warning);
                snapshot = new ProjectionSnapshot(
                    Interlocked.Increment(ref _revision),
                    MapErrorState(exception.Error),
                    rootPath,
                    Path.GetFileName(rootPath),
                    !string.IsNullOrWhiteSpace(_settings.RootPath),
                    [],
                    CreateLooseFiles([]),
                    [],
                    exception.Message,
                    DateTimeOffset.UtcNow);
                files = new Dictionary<string, ProjectionFileTarget>(StringComparer.OrdinalIgnoreCase);
            }

            lock (_gate)
            {
                _current = snapshot;
                _files = files;
            }

            SnapshotChanged?.Invoke(this, snapshot);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<bool> SetFolderOrderAsync(
        IReadOnlyList<string> orderedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Current;
            var available = current.Folders.Select(folder => folder.Id).ToArray();
            var merged = FolderOrderPolicy.Merge(available, orderedIds);
            if (merged.Count != available.Length)
            {
                return false;
            }

            _settings = _settings with { FolderOrder = merged };
            await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public async Task<bool> SetRootPathAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
        {
            return false;
        }

        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            await Task.Run(() => _scanner.Scan(normalized), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is RootScanException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _settings = _settings with { RootPath = normalized };
            await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public async Task<bool> UseDefaultRootAsync(
        CancellationToken cancellationToken = default)
    {
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _settings = _settings with { RootPath = null };
            await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public bool TryGetFile(string id, out ProjectionFileTarget? target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
        {
            return _files.TryGetValue(id, out target);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.Changed -= OnRootChanged;
        _watcher.Dispose();
        await _settingsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _refreshLock.WaitAsync().ConfigureAwait(false);
            _refreshLock.Release();
        }
        finally
        {
            _settingsLock.Release();
        }

        _refreshLock.Dispose();
        _settingsLock.Dispose();
    }

    private string ResolveRootPath() =>
        string.IsNullOrWhiteSpace(_settings.RootPath)
            ? _defaultRootPath
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(_settings.RootPath));

    private (ProjectionSnapshot Snapshot, Dictionary<string, ProjectionFileTarget> Files) MapSnapshot(
        DesktopSnapshot source,
        string? statusMessage)
    {
        var targets = new Dictionary<string, ProjectionFileTarget>(StringComparer.OrdinalIgnoreCase);
        var naturalFolders = source.Folders.Select(folder => folder.Id);
        var folderOrder = FolderOrderPolicy.Merge(naturalFolders, _settings.FolderOrder);
        var folderById = source.Folders.ToDictionary(folder => folder.Id, StringComparer.OrdinalIgnoreCase);
        var folders = folderOrder
            .Select(id => MapFolder(source.RootPath, folderById[id], targets))
            .ToArray();
        var looseFiles = CreateLooseFiles(
            source.RootFiles.Select(file => MapFile(source.RootPath, file, targets)).ToArray());
        var warnings = source.Warnings
            .Select(warning => new ProjectionWarning(
                warning.RelativePath,
                warning.Code.ToString()))
            .ToArray();
        var message = statusMessage
            ?? (warnings.Length > 0 ? $"{warnings.Length}개 항목을 읽지 못했습니다." : null);

        return (
            new ProjectionSnapshot(
                Interlocked.Increment(ref _revision),
                warnings.Length > 0 ? "warning" : "ready",
                source.RootPath,
                source.RootName,
                !string.IsNullOrWhiteSpace(_settings.RootPath),
                folders,
                looseFiles,
                warnings,
                message,
                source.CapturedAtUtc),
            targets);
    }

    private static ProjectionFolder MapFolder(
        string rootPath,
        DesktopFolder folder,
        IDictionary<string, ProjectionFileTarget> targets) =>
        new(
            folder.Id,
            folder.Name,
            folder.RelativePath,
            false,
            folder.Files.Select(file => MapFile(rootPath, file, targets)).ToArray());

    private static ProjectionFile MapFile(
        string rootPath,
        DesktopFile file,
        IDictionary<string, ProjectionFileTarget> targets)
    {
        var encodedId = Uri.EscapeDataString(file.Id);
        var projection = new ProjectionFile(
            file.Id,
            file.Name,
            file.RelativePath,
            file.Extension,
            file.Length,
            file.LastWriteTimeUtc,
            $"/visual/icon/{encodedId}",
            ThumbnailExtensions.Contains(file.Extension)
                ? $"/visual/thumbnail/{encodedId}"
                : null);
        var absolutePath = Path.GetFullPath(Path.Combine(rootPath, file.RelativePath));
        targets[file.Id] = new ProjectionFileTarget(file.Id, rootPath, absolutePath, projection);
        return projection;
    }

    private static ProjectionFolder CreateLooseFiles(IReadOnlyList<ProjectionFile> files) =>
        new("virtual:loose-files", "…", ".", true, files);

    private ProjectionSnapshot CreateLoadingSnapshot() =>
        new(
            Interlocked.Read(ref _revision),
            "loading",
            _defaultRootPath,
            Path.GetFileName(_defaultRootPath),
            false,
            [],
            CreateLooseFiles([]),
            [],
            "Desktop을 읽는 중입니다.",
            DateTimeOffset.UtcNow);

    private static string MapErrorState(RootScanError error) => error switch
    {
        RootScanError.DirectoryNotFound => "root-missing",
        RootScanError.AccessDenied => "access-denied",
        _ => "error",
    };

    private void OnRootChanged(object? sender, RootChangedEventArgs args)
    {
        _ = RefreshAfterWatcherAsync(args.Reason);
    }

    private async Task RefreshAfterWatcherAsync(RootChangeReason reason)
    {
        try
        {
            await RefreshAsync(
                reason == RootChangeReason.WatcherError
                    ? "파일 감시 오류 뒤 전체 재스캔했습니다."
                    : null).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Shutdown raced a queued watcher refresh.
        }
    }
}
