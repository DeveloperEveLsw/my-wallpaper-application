using Wallpaper.Core.FileOperations;
using Wallpaper.Core.Models;
using Wallpaper.Infrastructure.Windows.FileOperations;
using Wallpaper.Infrastructure.Windows.Settings;
using Wallpaper.Infrastructure.Windows.Watching;

namespace Wallpaper.Seelen.Tests;

public sealed class DesktopCommandServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        $"wallpaper-seelen-commands-{Guid.NewGuid():N}");
    private readonly string _settingsPath = Path.Combine(
        Path.GetTempPath(),
        $"wallpaper-seelen-command-settings-{Guid.NewGuid():N}");

    public DesktopCommandServiceTests() => Directory.CreateDirectory(_rootPath);

    [Fact]
    public async Task M3_OpenExplorerRenameAndRecycle_UseCurrentSafeTargets()
    {
        CreateFile(Path.Combine("Work", "report.txt"), "report");
        CreateFile(Path.Combine("RecycleFolder", "Nested", "keep.txt"), "nested");
        await using var projection = CreateProjection();
        await projection.InitializeAsync();
        var fileCommands = new LocalRecordingFileCommandService();
        var commands = new DesktopCommandService(projection, fileCommands);
        var work = Assert.Single(projection.Current.Folders, folder => folder.Name == "Work");
        var report = Assert.Single(work.Files);

        var open = await commands.ExecuteAsync(Request("open-1", "open", report.Id));
        var explorer = await commands.ExecuteAsync(
            Request("explorer-1", "showInExplorer", work.Id));
        var rename = await commands.ExecuteAsync(
            Request("rename-1", "rename", report.Id, newName: "summary.txt"));
        var recycleFolder = Assert.Single(
            projection.Current.Folders,
            folder => folder.Name == "RecycleFolder");
        var recycle = await commands.ExecuteAsync(
            Request("recycle-1", "recycle", recycleFolder.Id));

        Assert.True(open.Accepted);
        Assert.True(explorer.Accepted);
        Assert.True(rename.Accepted);
        Assert.True(recycle.Accepted);
        Assert.Equal(FileCommandItemKind.File, Assert.Single(fileCommands.Opened).Kind);
        Assert.Equal(FileCommandItemKind.Folder, Assert.Single(fileCommands.ShownInExplorer).Kind);
        Assert.True(File.Exists(Path.Combine(_rootPath, "Work", "summary.txt")));
        Assert.False(Directory.Exists(Path.Combine(_rootPath, "RecycleFolder")));
        Assert.DoesNotContain(
            projection.Current.Folders,
            folder => folder.Name == "RecycleFolder");
        var settings = await new JsonAppSettingsStore(_settingsPath).LoadAsync();
        Assert.DoesNotContain(recycleFolder.Id, settings.Settings.FolderOrder);
    }

    [Fact]
    public async Task M3_RenameFolder_PreservesItsSavedDockPositionAndNestedContents()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "A"));
        CreateFile(Path.Combine("RenameFolder", "Nested", "keep.txt"), "keep");
        Directory.CreateDirectory(Path.Combine(_rootPath, "Z"));
        await using var projection = CreateProjection();
        await projection.InitializeAsync();
        var requestedOrder = projection.Current.Folders
            .OrderByDescending(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .Select(folder => folder.Id)
            .ToArray();
        Assert.True(await projection.SetFolderOrderAsync(requestedOrder));
        var before = projection.Current.Folders.Select(folder => folder.Name).ToArray();
        var target = Assert.Single(
            projection.Current.Folders,
            folder => folder.Name == "RenameFolder");
        var commands = new DesktopCommandService(
            projection,
            new LocalRecordingFileCommandService());

        var result = await commands.ExecuteAsync(
            Request("rename-folder", "rename", target.Id, newName: "History"));

        Assert.True(result.Accepted);
        Assert.True(File.Exists(Path.Combine(_rootPath, "History", "Nested", "keep.txt")));
        var after = projection.Current.Folders.Select(folder => folder.Name).ToArray();
        Assert.Equal(Array.IndexOf(before, "RenameFolder"), Array.IndexOf(after, "History"));
        var stored = await new JsonAppSettingsStore(_settingsPath).LoadAsync();
        Assert.Contains(DesktopItemId.ForFolder("History"), stored.Settings.FolderOrder);
        Assert.DoesNotContain(target.Id, stored.Settings.FolderOrder);
    }

    [Fact]
    public async Task M3_InvalidAndStaleRename_ReturnErrorsAndRecoverTheSnapshot()
    {
        CreateFile("report.txt", "report");
        await using var projection = CreateProjection();
        await projection.InitializeAsync();
        var report = Assert.Single(projection.Current.LooseFiles.Files);
        var commands = new DesktopCommandService(
            projection,
            new LocalRecordingFileCommandService());

        var invalid = await commands.ExecuteAsync(
            Request("invalid-name", "rename", report.Id, newName: "CON.txt"));
        File.Delete(Path.Combine(_rootPath, "report.txt"));
        var stale = await commands.ExecuteAsync(
            Request("stale-name", "rename", report.Id, newName: "renamed.txt"));

        Assert.False(invalid.Accepted);
        Assert.Equal(nameof(FileCommandError.InvalidName), invalid.Code);
        Assert.False(stale.Accepted);
        Assert.Equal(nameof(FileCommandError.TargetMissing), stale.Code);
        Assert.Empty(projection.Current.LooseFiles.Files);
    }

    [Fact]
    public async Task M4_PrepareAndMove_CoversAllThreeRoutesAndCollisionProposal()
    {
        CreateFile("root-to-work.txt", "root");
        CreateFile(Path.Combine("Work", "work-to-root.txt"), "to root");
        CreateFile(Path.Combine("Work", "collision.txt"), "collision source");
        CreateFile(Path.Combine("Archive", "collision.txt"), "occupied zero");
        Directory.CreateDirectory(Path.Combine(_rootPath, "Archive", "collision (1).txt"));
        await using var projection = CreateProjection();
        await projection.InitializeAsync();
        var commands = new DesktopCommandService(
            projection,
            new LocalRecordingFileCommandService());

        var rootFile = Assert.Single(
            projection.Current.LooseFiles.Files,
            file => file.Name == "root-to-work.txt");
        var work = Assert.Single(projection.Current.Folders, folder => folder.Name == "Work");
        var rootToWork = await PrepareAndMoveAsync(
            commands,
            rootFile.Id,
            work.Id,
            "root-to-work.txt",
            "root-to-work");
        Assert.True(rootToWork.Accepted);
        Assert.True(File.Exists(Path.Combine(_rootPath, "Work", "root-to-work.txt")));

        work = Assert.Single(projection.Current.Folders, folder => folder.Name == "Work");
        var toRoot = Assert.Single(work.Files, file => file.Name == "work-to-root.txt");
        var workToRoot = await PrepareAndMoveAsync(
            commands,
            toRoot.Id,
            projection.Current.LooseFiles.Id,
            "work-to-root.txt",
            "work-to-root");
        Assert.True(workToRoot.Accepted);
        Assert.True(File.Exists(Path.Combine(_rootPath, "work-to-root.txt")));

        work = Assert.Single(projection.Current.Folders, folder => folder.Name == "Work");
        var archive = Assert.Single(
            projection.Current.Folders,
            folder => folder.Name == "Archive");
        var collision = Assert.Single(work.Files, file => file.Name == "collision.txt");
        var prepared = await commands.ExecuteAsync(
            Request(
                "collision-prepare",
                "prepareMove",
                collision.Id,
                archive.Id));

        Assert.True(prepared.Accepted);
        Assert.True(prepared.HasNameCollision);
        Assert.Equal("collision (2).txt", prepared.ProposedName);
        var moved = await commands.ExecuteAsync(
            Request(
                "collision-move",
                "move",
                collision.Id,
                archive.Id,
                "moved-collision.txt"));
        Assert.True(moved.Accepted);
        Assert.Equal(
            "collision source",
            File.ReadAllText(Path.Combine(_rootPath, "Archive", "moved-collision.txt")));
        Assert.Equal(
            "occupied zero",
            File.ReadAllText(Path.Combine(_rootPath, "Archive", "collision.txt")));
        Assert.True(Directory.Exists(Path.Combine(_rootPath, "Archive", "collision (1).txt")));
    }

    [Fact]
    public async Task M4_RechecksCollisionAndDeduplicatesARepeatedMoveRequest()
    {
        CreateFile(Path.Combine("Work", "race.txt"), "source");
        Directory.CreateDirectory(Path.Combine(_rootPath, "Archive"));
        await using var projection = CreateProjection();
        await projection.InitializeAsync();
        var commands = new DesktopCommandService(
            projection,
            new LocalRecordingFileCommandService());
        var work = Assert.Single(projection.Current.Folders, folder => folder.Name == "Work");
        var archive = Assert.Single(
            projection.Current.Folders,
            folder => folder.Name == "Archive");
        var source = Assert.Single(work.Files);
        var preparation = await commands.ExecuteAsync(
            Request("race-prepare", "prepareMove", source.Id, archive.Id));
        Assert.False(preparation.HasNameCollision);
        CreateFile(Path.Combine("Archive", "race.txt"), "external");

        var collision = await commands.ExecuteAsync(
            Request("race-move", "move", source.Id, archive.Id, preparation.ProposedName));

        Assert.False(collision.Accepted);
        Assert.Equal(nameof(FileCommandError.NameCollision), collision.Code);
        Assert.Equal("source", File.ReadAllText(Path.Combine(_rootPath, "Work", "race.txt")));
        Assert.Equal("external", File.ReadAllText(Path.Combine(_rootPath, "Archive", "race.txt")));

        File.Delete(Path.Combine(_rootPath, "Archive", "race.txt"));
        await projection.RefreshAsync();
        work = Assert.Single(projection.Current.Folders, folder => folder.Name == "Work");
        archive = Assert.Single(projection.Current.Folders, folder => folder.Name == "Archive");
        source = Assert.Single(work.Files);
        var request = Request(
            "deduplicated-move",
            "move",
            source.Id,
            archive.Id,
            "race.txt");
        var firstTask = commands.ExecuteAsync(request);
        var duplicateTask = commands.ExecuteAsync(request);
        var first = await firstTask;
        var duplicate = await duplicateTask;

        Assert.Same(firstTask, duplicateTask);
        Assert.Equal(first, duplicate);
        Assert.True(first.Accepted);
        Assert.False(File.Exists(Path.Combine(_rootPath, "Work", "race.txt")));
        Assert.Equal("source", File.ReadAllText(Path.Combine(_rootPath, "Archive", "race.txt")));
    }

    [Fact]
    public async Task M4_RejectsFolderSourceSameCardAndInvalidEditedName()
    {
        CreateFile(Path.Combine("Work", "report.txt"), "report");
        Directory.CreateDirectory(Path.Combine(_rootPath, "Archive"));
        await using var projection = CreateProjection();
        await projection.InitializeAsync();
        var commands = new DesktopCommandService(
            projection,
            new LocalRecordingFileCommandService());
        var work = Assert.Single(projection.Current.Folders, folder => folder.Name == "Work");
        var archive = Assert.Single(
            projection.Current.Folders,
            folder => folder.Name == "Archive");
        var report = Assert.Single(work.Files);

        var folderSource = await commands.ExecuteAsync(
            Request("folder-source", "prepareMove", work.Id, archive.Id));
        var sameCard = await commands.ExecuteAsync(
            Request("same-card", "prepareMove", report.Id, work.Id));
        var invalidName = await commands.ExecuteAsync(
            Request("invalid-move-name", "move", report.Id, archive.Id, "bad?.txt"));
        var virtualSource = await commands.ExecuteAsync(
            Request(
                "virtual-source",
                "prepareMove",
                projection.Current.LooseFiles.Id,
                archive.Id));

        Assert.False(folderSource.Accepted);
        Assert.Equal("invalid_target", folderSource.Code);
        Assert.False(sameCard.Accepted);
        Assert.Equal(nameof(FileCommandError.NoChange), sameCard.Code);
        Assert.False(invalidName.Accepted);
        Assert.Equal(nameof(FileCommandError.InvalidName), invalidName.Code);
        Assert.False(virtualSource.Accepted);
        Assert.Equal("target_missing", virtualSource.Code);
        Assert.True(File.Exists(Path.Combine(_rootPath, "Work", "report.txt")));
    }

    [Fact]
    public async Task ReusingARequestIdForDifferentCommands_IsRejected()
    {
        CreateFile("one.txt", "one");
        CreateFile("two.txt", "two");
        await using var projection = CreateProjection();
        await projection.InitializeAsync();
        var files = projection.Current.LooseFiles.Files;
        var commands = new DesktopCommandService(
            projection,
            new LocalRecordingFileCommandService());

        var first = await commands.ExecuteAsync(Request("same-id", "open", files[0].Id));
        var conflicting = await commands.ExecuteAsync(Request("same-id", "open", files[1].Id));

        Assert.True(first.Accepted);
        Assert.False(conflicting.Accepted);
        Assert.Equal("duplicate_request", conflicting.Code);
    }

    [Fact]
    public async Task RootChanges_WaitForAnInFlightItemCommand()
    {
        CreateFile("report.txt", "report");
        var alternateRoot = Path.Combine(
            Path.GetTempPath(),
            $"wallpaper-seelen-alternate-command-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(alternateRoot);
        try
        {
            await using var projection = CreateProjection();
            await projection.InitializeAsync();
            var file = Assert.Single(projection.Current.LooseFiles.Files);
            var blocking = new BlockingOpenFileCommandService();
            var commands = new DesktopCommandService(projection, blocking);

            var openTask = commands.ExecuteAsync(Request("blocking-open", "open", file.Id));
            await blocking.OpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var setRootTask = commands.SetRootPathAsync(alternateRoot);

            Assert.False(setRootTask.IsCompleted);
            blocking.ReleaseOpen.TrySetResult();
            Assert.True((await openTask).Accepted);
            Assert.True(await setRootTask);
            Assert.Equal(Path.GetFullPath(alternateRoot), projection.Current.RootPath);
        }
        finally
        {
            Directory.Delete(alternateRoot, recursive: true);
        }
    }

    [Fact]
    public async Task M5_ShellMenuPreparation_RevalidatesTheCurrentProjectedTarget()
    {
        CreateFile(Path.Combine("Work", "report.txt"), "report");
        await using var projection = CreateProjection();
        await projection.InitializeAsync();
        var commands = new DesktopCommandService(
            projection,
            new LocalRecordingFileCommandService());
        var report = Assert.Single(
            Assert.Single(projection.Current.Folders).Files);
        var request = new DesktopShellMenuRequest(
            "shell-menu-current",
            report.Id,
            100,
            200,
            8192);

        var current = await commands.PrepareShellMenuTargetAsync(request);
        File.Delete(Path.Combine(_rootPath, "Work", "report.txt"));
        var stale = await commands.PrepareShellMenuTargetAsync(
            request with { RequestId = "shell-menu-stale" });

        Assert.True(current.Accepted);
        Assert.NotNull(current.Target);
        Assert.Equal(
            "Work/report.txt",
            current.Target.RelativePath.Replace('\\', '/'));
        Assert.False(stale.Accepted);
        Assert.Equal(nameof(FileCommandError.TargetMissing), stale.Code);
        Assert.Empty(Assert.Single(projection.Current.Folders).Files);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        if (Directory.Exists(_settingsPath))
        {
            Directory.Delete(_settingsPath, recursive: true);
        }
    }

    private DesktopProjectionService CreateProjection() =>
        new(
            _rootPath,
            watcher: new NoopRootChangeWatcher(),
            settingsStore: new JsonAppSettingsStore(_settingsPath));

    private string CreateFile(string relativePath, string contents)
    {
        var path = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private static DesktopCommandRequest Request(
        string requestId,
        string action,
        string itemId,
        string? destinationId = null,
        string? newName = null) =>
        new(requestId, action, itemId, destinationId, newName);

    private static async Task<DesktopCommandResult> PrepareAndMoveAsync(
        DesktopCommandService commands,
        string itemId,
        string destinationId,
        string expectedName,
        string requestPrefix)
    {
        var prepared = await commands.ExecuteAsync(
            Request($"{requestPrefix}-prepare", "prepareMove", itemId, destinationId));
        Assert.True(prepared.Accepted);
        Assert.False(prepared.HasNameCollision);
        Assert.Equal(expectedName, prepared.ProposedName);
        return await commands.ExecuteAsync(
            Request(
                $"{requestPrefix}-move",
                "move",
                itemId,
                destinationId,
                prepared.ProposedName));
    }

    private sealed class NoopRootChangeWatcher : IRootChangeWatcher
    {
        public event EventHandler<RootChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public RootWatchStatus Watch(string rootPath) => new(true, true, null);

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class LocalRecordingFileCommandService : IFileCommandService
    {
        private readonly WindowsFileCommandService _inner = new();

        public List<FileCommandTarget> Opened { get; } = [];

        public List<FileCommandTarget> ShownInExplorer { get; } = [];

        public async Task EnsureValidAsync(
            FileCommandTarget target,
            CancellationToken cancellationToken = default) =>
            await _inner.EnsureValidAsync(target, cancellationToken);

        public async Task OpenAsync(
            FileCommandTarget target,
            CancellationToken cancellationToken = default)
        {
            await EnsureValidAsync(target, cancellationToken);
            Opened.Add(target);
        }

        public async Task ShowInExplorerAsync(
            FileCommandTarget target,
            CancellationToken cancellationToken = default)
        {
            await EnsureValidAsync(target, cancellationToken);
            ShownInExplorer.Add(target);
        }

        public Task<FileCommandTarget> RenameAsync(
            FileCommandTarget target,
            string newName,
            CancellationToken cancellationToken = default) =>
            _inner.RenameAsync(target, newName, cancellationToken);

        public Task<FileMovePreparation> PrepareMoveAsync(
            FileCommandTarget source,
            FileMoveDestination destination,
            string desiredName,
            CancellationToken cancellationToken = default) =>
            _inner.PrepareMoveAsync(source, destination, desiredName, cancellationToken);

        public Task<FileCommandTarget> MoveAsync(
            FileCommandTarget source,
            FileMoveDestination destination,
            string destinationName,
            CancellationToken cancellationToken = default) =>
            _inner.MoveAsync(source, destination, destinationName, cancellationToken);

        public async Task RecycleAsync(
            FileCommandTarget target,
            CancellationToken cancellationToken = default)
        {
            var validated = FileCommandTargetValidator.ValidateExisting(target);
            cancellationToken.ThrowIfCancellationRequested();
            if (target.Kind == FileCommandItemKind.File)
            {
                File.Delete(validated.AbsolutePath);
            }
            else
            {
                Directory.Delete(validated.AbsolutePath, recursive: true);
            }

            await Task.CompletedTask;
        }
    }

    private sealed class BlockingOpenFileCommandService : IFileCommandService
    {
        private readonly LocalRecordingFileCommandService _inner = new();

        public TaskCompletionSource OpenStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseOpen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnsureValidAsync(
            FileCommandTarget target,
            CancellationToken cancellationToken = default) =>
            _inner.EnsureValidAsync(target, cancellationToken);

        public async Task OpenAsync(
            FileCommandTarget target,
            CancellationToken cancellationToken = default)
        {
            await EnsureValidAsync(target, cancellationToken);
            OpenStarted.TrySetResult();
            await ReleaseOpen.Task.WaitAsync(cancellationToken);
        }

        public Task ShowInExplorerAsync(
            FileCommandTarget target,
            CancellationToken cancellationToken = default) =>
            _inner.ShowInExplorerAsync(target, cancellationToken);

        public Task<FileCommandTarget> RenameAsync(
            FileCommandTarget target,
            string newName,
            CancellationToken cancellationToken = default) =>
            _inner.RenameAsync(target, newName, cancellationToken);

        public Task<FileMovePreparation> PrepareMoveAsync(
            FileCommandTarget source,
            FileMoveDestination destination,
            string desiredName,
            CancellationToken cancellationToken = default) =>
            _inner.PrepareMoveAsync(source, destination, desiredName, cancellationToken);

        public Task<FileCommandTarget> MoveAsync(
            FileCommandTarget source,
            FileMoveDestination destination,
            string destinationName,
            CancellationToken cancellationToken = default) =>
            _inner.MoveAsync(source, destination, destinationName, cancellationToken);

        public Task RecycleAsync(
            FileCommandTarget target,
            CancellationToken cancellationToken = default) =>
            _inner.RecycleAsync(target, cancellationToken);
    }
}
