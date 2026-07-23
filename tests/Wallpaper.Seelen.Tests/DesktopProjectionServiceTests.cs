using Wallpaper.Core.FileOperations;
using Wallpaper.Infrastructure.Windows.Settings;

namespace Wallpaper.Seelen.Tests;

public sealed class DesktopProjectionServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"wallpaper-seelen-{Guid.NewGuid():N}");
    private readonly string _settingsRoot = Path.Combine(
        Path.GetTempPath(),
        $"wallpaper-seelen-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task Initialize_ProjectsFoldersLooseFilesAndVisualPaths()
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, "Work"));
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "note.txt"), "hello");
        await File.WriteAllBytesAsync(Path.Combine(_testRoot, "photo.png"), [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "Work", "inside.md"), "inside");
        await using var service = CreateService();

        await service.InitializeAsync();

        var snapshot = service.Current;
        Assert.Equal("ready", snapshot.State);
        Assert.False(snapshot.RootConfigured);
        Assert.Single(snapshot.Folders);
        Assert.Equal("Work", snapshot.Folders[0].Name);
        Assert.Equal(2, snapshot.LooseFiles.Files.Count);
        Assert.Contains(snapshot.LooseFiles.Files, file => file.ThumbnailPath is not null);
        Assert.All(snapshot.LooseFiles.Files, file => Assert.StartsWith("/visual/icon/", file.IconPath));
        var note = Assert.Single(snapshot.LooseFiles.Files, file => file.Name == "note.txt");
        Assert.True(service.TryGetFile(note.Id, out var target));
        Assert.NotNull(target);
        Assert.Equal(snapshot.RootPath, target.RootPath);
        Assert.Equal(Path.Combine(snapshot.RootPath, "note.txt"), target.AbsolutePath);
        Assert.True(service.TryGetItem(note.Id, out var noteTarget));
        Assert.Equal(FileCommandItemKind.File, noteTarget!.Kind);
        var work = Assert.Single(snapshot.Folders);
        Assert.True(service.TryGetItem(work.Id, out var workTarget));
        Assert.Equal(FileCommandItemKind.Folder, workTarget!.Kind);
        Assert.Equal("Work", workTarget.RelativePath);
        Assert.True(service.TryGetMoveDestination(work.Id, out var workDestination));
        Assert.Equal("Work", workDestination!.RelativeFolderPath);
        Assert.True(service.TryGetMoveDestination(
            snapshot.LooseFiles.Id,
            out var rootDestination));
        Assert.Null(rootDestination!.RelativeFolderPath);
        Assert.False(service.TryGetItem(snapshot.LooseFiles.Id, out _));
    }

    [Fact]
    public async Task Watcher_RefreshesAfterAProjectedFileChange()
    {
        Directory.CreateDirectory(_testRoot);
        await using var service = CreateService();
        var changed = new TaskCompletionSource<ProjectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await service.InitializeAsync();
        service.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.LooseFiles.Files.Any(file => file.Name == "new.txt"))
            {
                changed.TrySetResult(snapshot);
            }
        };

        await File.WriteAllTextAsync(Path.Combine(_testRoot, "new.txt"), "new");
        var refreshed = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(refreshed.LooseFiles.Files, file => file.Name == "new.txt");
    }

    [Fact]
    public async Task SetFolderOrder_PersistsAndRestoresTheUserOrder()
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, "A"));
        Directory.CreateDirectory(Path.Combine(_testRoot, "B"));
        var settingsDirectory = _settingsRoot;
        await using (var first = CreateService(settingsDirectory))
        {
            await first.InitializeAsync();
            var reversed = first.Current.Folders.Select(folder => folder.Id).Reverse().ToArray();
            Assert.True(await first.SetFolderOrderAsync(reversed));
            Assert.Equal(reversed, first.Current.Folders.Select(folder => folder.Id));
        }

        await using var second = CreateService(settingsDirectory);
        await second.InitializeAsync();
        Assert.Equal(["B", "A"], second.Current.Folders.Select(folder => folder.Name));
    }

    [Fact]
    public async Task Initialize_RecoversCorruptedSettings()
    {
        Directory.CreateDirectory(_testRoot);
        var settingsDirectory = _settingsRoot;
        Directory.CreateDirectory(settingsDirectory);
        await File.WriteAllTextAsync(Path.Combine(settingsDirectory, "settings.json"), "{broken");
        await using var service = CreateService(settingsDirectory);

        await service.InitializeAsync();

        Assert.Equal("ready", service.Current.State);
        Assert.Contains("손상된 설정", service.Current.Message);
        var reloaded = await new JsonAppSettingsStore(settingsDirectory).LoadAsync();
        Assert.False(reloaded.WasCorrupted);
    }

    [Fact]
    public async Task SetRootPath_RejectsInvalidPathAndPersistsAValidRoot()
    {
        Directory.CreateDirectory(_testRoot);
        var alternate = Path.Combine(
            Path.GetTempPath(),
            $"wallpaper-seelen-alternate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(alternate);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(alternate, "alternate.txt"), "ok");
            await using var service = CreateService();
            await service.InitializeAsync();

            Assert.False(await service.SetRootPathAsync("relative"));
            Assert.True(await service.SetRootPathAsync(alternate));
            Assert.Equal(Path.GetFullPath(alternate), service.Current.RootPath);
            Assert.True(service.Current.RootConfigured);
            Assert.Contains(
                service.Current.LooseFiles.Files,
                file => file.Name == "alternate.txt");
        }
        finally
        {
            Directory.Delete(alternate, recursive: true);
        }
    }

    [Fact]
    public async Task UseDefaultRoot_ClearsTheCustomRootAndPersistsTheDefaultChoice()
    {
        Directory.CreateDirectory(_testRoot);
        var alternate = Path.Combine(
            Path.GetTempPath(),
            $"wallpaper-seelen-alternate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(alternate);
        try
        {
            await using (var first = CreateService())
            {
                await first.InitializeAsync();
                Assert.True(await first.SetRootPathAsync(alternate));
                Assert.True(first.Current.RootConfigured);

                Assert.True(await first.UseDefaultRootAsync());

                Assert.False(first.Current.RootConfigured);
                Assert.Equal(Path.GetFullPath(_testRoot), first.Current.RootPath);
            }

            await using var second = CreateService();
            await second.InitializeAsync();
            Assert.False(second.Current.RootConfigured);
            Assert.Equal(Path.GetFullPath(_testRoot), second.Current.RootPath);
        }
        finally
        {
            Directory.Delete(alternate, recursive: true);
        }
    }

    [Fact]
    public async Task Projection_HandlesTwelveHundredFilesWithoutDroppingItems()
    {
        Directory.CreateDirectory(_testRoot);
        for (var index = 0; index < 1200; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_testRoot, $"item-{index:D4}.txt"),
                index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        await using var service = CreateService();

        await service.InitializeAsync();

        Assert.Equal(1200, service.Current.LooseFiles.Files.Count);
    }

    [Fact]
    public async Task Watcher_ReportsRootDeletionAndRecoversWhenItReturns()
    {
        Directory.CreateDirectory(_testRoot);
        var movedRoot = $"{_testRoot}-temporarily-missing";
        await using var service = CreateService();
        var missing = new TaskCompletionSource<ProjectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource<ProjectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await service.InitializeAsync();
        service.SnapshotChanged += (_, current) =>
        {
            if (current.State == "root-missing")
            {
                missing.TrySetResult(current);
            }
            else if (missing.Task.IsCompleted && current.State == "ready")
            {
                recovered.TrySetResult(current);
            }
        };

        Directory.Move(_testRoot, movedRoot);
        await missing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Directory.Move(movedRoot, _testRoot);
        var restored = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(Path.GetFullPath(_testRoot), restored.RootPath);
        Assert.True(service.WatchStatus.ContentWatching);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }

        if (Directory.Exists(_settingsRoot))
        {
            Directory.Delete(_settingsRoot, recursive: true);
        }
    }

    private DesktopProjectionService CreateService(string? settingsDirectory = null)
    {
        Directory.CreateDirectory(_testRoot);
        return new DesktopProjectionService(
            _testRoot,
            settingsStore: new JsonAppSettingsStore(
                settingsDirectory ?? _settingsRoot));
    }
}
