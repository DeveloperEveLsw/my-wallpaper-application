using Wallpaper.Infrastructure.Windows.Watching;

namespace Wallpaper.Infrastructure.Windows.Tests;

public sealed class DebouncedFileSystemWatcherTests : IDisposable
{
    private readonly string _parentPath = Path.Combine(
        Path.GetTempPath(),
        $"wallpaper-watcher-{Guid.NewGuid():N}");

    [Fact]
    public async Task Watch_CoalescesRapidProjectedChangesIntoOneSignal()
    {
        var rootPath = Path.Combine(_parentPath, "root");
        Directory.CreateDirectory(rootPath);
        using var watcher = new DebouncedFileSystemWatcher(TimeSpan.FromMilliseconds(120));
        var signals = new List<RootChangeReason>();
        var signalReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, args) =>
        {
            lock (signals)
            {
                signals.Add(args.Reason);
            }

            signalReceived.TrySetResult();
        };

        var status = watcher.Watch(rootPath);
        await File.WriteAllTextAsync(Path.Combine(rootPath, "one.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "two.txt"), "2");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "three.txt"), "3");

        await signalReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300);

        Assert.True(status.IsContentWatching);
        lock (signals)
        {
            Assert.Single(signals);
            Assert.Equal(RootChangeReason.ContentChanged, signals[0]);
        }
    }

    [Fact]
    public async Task Watch_ReportsWhenTheRootIsDeleted()
    {
        var rootPath = Path.Combine(_parentPath, "root");
        Directory.CreateDirectory(rootPath);
        using var watcher = new DebouncedFileSystemWatcher(TimeSpan.FromMilliseconds(80));
        var signalReceived = new TaskCompletionSource<RootChangeReason>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, args) => signalReceived.TrySetResult(args.Reason);

        var status = watcher.Watch(rootPath);
        Directory.Delete(rootPath);

        var reason = await signalReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(status.IsParentWatching);
        Assert.NotEqual(RootChangeReason.ContentChanged, reason);
    }

    [Fact]
    public async Task Watch_UsesTheParentToDetectCreationWhenTheRootIsMissing()
    {
        Directory.CreateDirectory(_parentPath);
        var rootPath = Path.Combine(_parentPath, "root");
        using var watcher = new DebouncedFileSystemWatcher(TimeSpan.FromMilliseconds(80));
        var signalReceived = new TaskCompletionSource<RootChangeReason>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, args) => signalReceived.TrySetResult(args.Reason);

        var status = watcher.Watch(rootPath);
        Directory.CreateDirectory(rootPath);

        var reason = await signalReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(status.IsContentWatching);
        Assert.True(status.IsParentWatching);
        Assert.Equal(RootChangeReason.RootAvailabilityChanged, reason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_parentPath))
        {
            Directory.Delete(_parentPath, recursive: true);
        }
    }
}
