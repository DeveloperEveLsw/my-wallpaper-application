using Wallpaper.Core.FileOperations;
using Wallpaper.Infrastructure.Windows.FileOperations;

namespace Wallpaper.Infrastructure.Windows.Tests;

public sealed class WindowsFileCommandServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        $"wallpaper-file-commands-{Guid.NewGuid():N}");

    public WindowsFileCommandServiceTests() => Directory.CreateDirectory(_rootPath);

    [Fact]
    public async Task RenameAsync_RenamesAFileWithoutChangingItsContents()
    {
        var sourcePath = CreateFile(Path.Combine("Work", "report.txt"), "M3 report");
        var service = new WindowsFileCommandService();

        var renamed = await service.RenameAsync(
            new FileCommandTarget(_rootPath, "Work/report.txt", FileCommandItemKind.File),
            "summary.txt");

        Assert.False(File.Exists(sourcePath));
        Assert.Equal("M3 report", File.ReadAllText(Path.Combine(_rootPath, "Work", "summary.txt")));
        Assert.Equal("Work/summary.txt", renamed.RelativePath);
    }

    [Fact]
    public async Task RenameAsync_RenamesADirectChildFolderWithNestedContents()
    {
        CreateFile(Path.Combine("Archive", "Nested", "keep.txt"), "keep");
        var service = new WindowsFileCommandService();

        var renamed = await service.RenameAsync(
            new FileCommandTarget(_rootPath, "Archive", FileCommandItemKind.Folder),
            "History");

        Assert.False(Directory.Exists(Path.Combine(_rootPath, "Archive")));
        Assert.True(File.Exists(Path.Combine(_rootPath, "History", "Nested", "keep.txt")));
        Assert.Equal("History", renamed.RelativePath);
    }

    [Fact]
    public async Task RenameAsync_RejectsCollisionWithoutChangingEitherFile()
    {
        CreateFile("first.txt", "first");
        CreateFile("second.txt", "second");
        var service = new WindowsFileCommandService();

        var exception = await Assert.ThrowsAsync<FileCommandException>(() => service.RenameAsync(
            new FileCommandTarget(_rootPath, "first.txt", FileCommandItemKind.File),
            "second.txt"));

        Assert.Equal(FileCommandError.NameCollision, exception.Error);
        Assert.Equal("first", File.ReadAllText(Path.Combine(_rootPath, "first.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_rootPath, "second.txt")));
    }

    [Fact]
    public async Task RenameAsync_RejectsInvalidOrStaleTargets()
    {
        CreateFile("report.txt", "report");
        var service = new WindowsFileCommandService();

        var invalidName = await Assert.ThrowsAsync<FileCommandException>(() => service.RenameAsync(
            new FileCommandTarget(_rootPath, "report.txt", FileCommandItemKind.File),
            "bad?.txt"));
        var stale = await Assert.ThrowsAsync<FileCommandException>(() => service.RenameAsync(
            new FileCommandTarget(_rootPath, "missing.txt", FileCommandItemKind.File),
            "renamed.txt"));

        Assert.Equal(FileCommandError.InvalidName, invalidName.Error);
        Assert.Equal(FileCommandError.TargetMissing, stale.Error);
        Assert.True(File.Exists(Path.Combine(_rootPath, "report.txt")));
    }

    [Fact]
    public async Task RenameAsync_SupportsCaseOnlyRenameOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        CreateFile("CaseName.txt", "case");
        var service = new WindowsFileCommandService();

        var renamed = await service.RenameAsync(
            new FileCommandTarget(_rootPath, "CaseName.txt", FileCommandItemKind.File),
            "casename.txt");

        Assert.Equal("casename.txt", renamed.RelativePath);
        Assert.Equal("casename.txt", new FileInfo(Path.Combine(_rootPath, "casename.txt")).Name);
    }

    [Fact]
    public async Task RenameAsync_LeavesLockedWindowsFileUnchanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourcePath = CreateFile("locked.txt", "locked");
        var service = new WindowsFileCommandService();
        await using var lockStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var exception = await Assert.ThrowsAsync<FileCommandException>(() => service.RenameAsync(
            new FileCommandTarget(_rootPath, "locked.txt", FileCommandItemKind.File),
            "renamed.txt"));

        Assert.Equal(FileCommandError.RenameFailed, exception.Error);
        Assert.True(File.Exists(sourcePath));
        Assert.False(File.Exists(Path.Combine(_rootPath, "renamed.txt")));
    }

    [Fact]
    public async Task MoveAsync_MovesRootFileIntoDirectChildFolder()
    {
        var sourcePath = CreateFile("root-file.txt", "root to folder");
        Directory.CreateDirectory(Path.Combine(_rootPath, "Work"));
        var service = new WindowsFileCommandService();

        var moved = await service.MoveAsync(
            new FileCommandTarget(_rootPath, "root-file.txt", FileCommandItemKind.File),
            new FileMoveDestination(_rootPath, "Work"),
            "root-file.txt");

        Assert.False(File.Exists(sourcePath));
        Assert.Equal("root to folder", File.ReadAllText(Path.Combine(_rootPath, "Work", "root-file.txt")));
        Assert.Equal("Work/root-file.txt", moved.RelativePath);
    }

    [Fact]
    public async Task MoveAsync_MovesFolderFileToRoot()
    {
        var sourcePath = CreateFile(Path.Combine("Work", "report.txt"), "folder to root");
        var service = new WindowsFileCommandService();

        var moved = await service.MoveAsync(
            new FileCommandTarget(_rootPath, "Work/report.txt", FileCommandItemKind.File),
            new FileMoveDestination(_rootPath, null),
            "report.txt");

        Assert.False(File.Exists(sourcePath));
        Assert.Equal("folder to root", File.ReadAllText(Path.Combine(_rootPath, "report.txt")));
        Assert.Equal("report.txt", moved.RelativePath);
    }

    [Fact]
    public async Task MoveAsync_MovesFileBetweenDirectChildFolders()
    {
        var sourcePath = CreateFile(Path.Combine("Work", "notes.txt"), "folder to folder");
        Directory.CreateDirectory(Path.Combine(_rootPath, "Archive"));
        var service = new WindowsFileCommandService();

        var moved = await service.MoveAsync(
            new FileCommandTarget(_rootPath, "Work/notes.txt", FileCommandItemKind.File),
            new FileMoveDestination(_rootPath, "Archive"),
            "renamed-notes.txt");

        Assert.False(File.Exists(sourcePath));
        Assert.Equal(
            "folder to folder",
            File.ReadAllText(Path.Combine(_rootPath, "Archive", "renamed-notes.txt")));
        Assert.Equal("Archive/renamed-notes.txt", moved.RelativePath);
    }

    [Fact]
    public async Task PrepareMoveAsync_ProposesSmallestAvailableNameIncludingFolderCollisions()
    {
        CreateFile(Path.Combine("Work", "photo.png"), "source");
        CreateFile(Path.Combine("Photos", "photo.png"), "collision zero");
        Directory.CreateDirectory(Path.Combine(_rootPath, "Photos", "photo (1).png"));
        CreateFile(Path.Combine("Photos", "photo (3).png"), "collision three");
        var service = new WindowsFileCommandService();

        var preparation = await service.PrepareMoveAsync(
            new FileCommandTarget(_rootPath, "Work/photo.png", FileCommandItemKind.File),
            new FileMoveDestination(_rootPath, "Photos"),
            "photo.png");

        Assert.True(preparation.HasNameCollision);
        Assert.Equal("photo (2).png", preparation.ProposedName);
    }

    [Fact]
    public async Task MoveAsync_RejectsCollisionWithoutOverwritingEitherEntry()
    {
        CreateFile(Path.Combine("Work", "same.txt"), "source");
        CreateFile(Path.Combine("Archive", "same.txt"), "destination");
        var service = new WindowsFileCommandService();

        var exception = await Assert.ThrowsAsync<FileCommandException>(() => service.MoveAsync(
            new FileCommandTarget(_rootPath, "Work/same.txt", FileCommandItemKind.File),
            new FileMoveDestination(_rootPath, "Archive"),
            "same.txt"));

        Assert.Equal(FileCommandError.NameCollision, exception.Error);
        Assert.Equal("source", File.ReadAllText(Path.Combine(_rootPath, "Work", "same.txt")));
        Assert.Equal("destination", File.ReadAllText(Path.Combine(_rootPath, "Archive", "same.txt")));
    }

    [Fact]
    public async Task MoveAsync_RechecksCollisionCreatedAfterPreparation()
    {
        CreateFile(Path.Combine("Work", "race.txt"), "source");
        Directory.CreateDirectory(Path.Combine(_rootPath, "Archive"));
        var service = new WindowsFileCommandService();
        var source = new FileCommandTarget(
            _rootPath,
            "Work/race.txt",
            FileCommandItemKind.File);
        var destination = new FileMoveDestination(_rootPath, "Archive");

        var preparation = await service.PrepareMoveAsync(source, destination, "race.txt");
        Assert.False(preparation.HasNameCollision);
        CreateFile(Path.Combine("Archive", "race.txt"), "external collision");

        var exception = await Assert.ThrowsAsync<FileCommandException>(() =>
            service.MoveAsync(source, destination, preparation.ProposedName));

        Assert.Equal(FileCommandError.NameCollision, exception.Error);
        Assert.Equal("source", File.ReadAllText(Path.Combine(_rootPath, "Work", "race.txt")));
        Assert.Equal("external collision", File.ReadAllText(Path.Combine(_rootPath, "Archive", "race.txt")));
    }

    [Fact]
    public async Task MoveCommands_RejectSameContainerNestedDestinationAndStaleEndpoints()
    {
        CreateFile(Path.Combine("Work", "report.txt"), "source");
        var service = new WindowsFileCommandService();
        var source = new FileCommandTarget(_rootPath, "Work/report.txt", FileCommandItemKind.File);

        var sameContainer = await Assert.ThrowsAsync<FileCommandException>(() =>
            service.PrepareMoveAsync(source, new FileMoveDestination(_rootPath, "Work"), "report.txt"));
        var nested = await Assert.ThrowsAsync<FileCommandException>(() =>
            service.PrepareMoveAsync(source, new FileMoveDestination(_rootPath, "Work/Nested"), "report.txt"));
        var staleSource = await Assert.ThrowsAsync<FileCommandException>(() =>
            service.MoveAsync(
                source with { RelativePath = "Work/missing.txt" },
                new FileMoveDestination(_rootPath, null),
                "missing.txt"));
        var staleDestination = await Assert.ThrowsAsync<FileCommandException>(() =>
            service.MoveAsync(source, new FileMoveDestination(_rootPath, "Missing"), "report.txt"));

        Assert.Equal(FileCommandError.NoChange, sameContainer.Error);
        Assert.Equal(FileCommandError.InvalidTarget, nested.Error);
        Assert.Equal(FileCommandError.TargetMissing, staleSource.Error);
        Assert.Equal(FileCommandError.TargetMissing, staleDestination.Error);
        Assert.True(File.Exists(Path.Combine(_rootPath, "Work", "report.txt")));
    }

    [Fact]
    public async Task MoveAsync_RejectsDestinationFromAnotherRoot()
    {
        CreateFile(Path.Combine("Work", "report.txt"), "source");
        var otherRoot = Path.Combine(Path.GetTempPath(), $"wallpaper-other-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(otherRoot);
        var service = new WindowsFileCommandService();

        try
        {
            var exception = await Assert.ThrowsAsync<FileCommandException>(() => service.MoveAsync(
                new FileCommandTarget(_rootPath, "Work/report.txt", FileCommandItemKind.File),
                new FileMoveDestination(otherRoot, null),
                "report.txt"));

            Assert.Equal(FileCommandError.InvalidTarget, exception.Error);
            Assert.True(File.Exists(Path.Combine(_rootPath, "Work", "report.txt")));
        }
        finally
        {
            Directory.Delete(otherRoot, recursive: true);
        }
    }

    [Fact]
    public async Task MoveAsync_RejectsFolderSourceAndInvalidEditedName()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "Work"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "Archive"));
        var service = new WindowsFileCommandService();

        var folder = await Assert.ThrowsAsync<FileCommandException>(() => service.MoveAsync(
            new FileCommandTarget(_rootPath, "Work", FileCommandItemKind.Folder),
            new FileMoveDestination(_rootPath, "Archive"),
            "Work"));
        CreateFile(Path.Combine("Work", "report.txt"), "source");
        var invalidName = await Assert.ThrowsAsync<FileCommandException>(() => service.MoveAsync(
            new FileCommandTarget(_rootPath, "Work/report.txt", FileCommandItemKind.File),
            new FileMoveDestination(_rootPath, "Archive"),
            "bad?.txt"));

        Assert.Equal(FileCommandError.InvalidTarget, folder.Error);
        Assert.Equal(FileCommandError.InvalidName, invalidName.Error);
        Assert.True(File.Exists(Path.Combine(_rootPath, "Work", "report.txt")));
    }

    [Fact]
    public async Task MoveAsync_LeavesLockedWindowsFileUnchanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sourcePath = CreateFile(Path.Combine("Work", "locked.txt"), "locked");
        Directory.CreateDirectory(Path.Combine(_rootPath, "Archive"));
        var service = new WindowsFileCommandService();
        await using var lockStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var exception = await Assert.ThrowsAsync<FileCommandException>(() => service.MoveAsync(
            new FileCommandTarget(_rootPath, "Work/locked.txt", FileCommandItemKind.File),
            new FileMoveDestination(_rootPath, "Archive"),
            "locked.txt"));

        Assert.Equal(FileCommandError.MoveFailed, exception.Error);
        Assert.True(File.Exists(sourcePath));
        Assert.False(File.Exists(Path.Combine(_rootPath, "Archive", "locked.txt")));
    }

    [Fact]
    public async Task ShellCommands_RejectStaleTargetBeforeStartingWindowsShell()
    {
        var service = new WindowsFileCommandService();
        var staleTarget = new FileCommandTarget(
            _rootPath,
            "missing.txt",
            FileCommandItemKind.File);

        var openException = await Assert.ThrowsAsync<FileCommandException>(() =>
            service.OpenAsync(staleTarget));

        Assert.Equal(FileCommandError.TargetMissing, openException.Error);
        if (OperatingSystem.IsWindows())
        {
            var explorerException = await Assert.ThrowsAsync<FileCommandException>(() =>
                service.ShowInExplorerAsync(staleTarget));
            Assert.Equal(FileCommandError.TargetMissing, explorerException.Error);
        }
    }

    [Fact]
    public async Task RecycleAsync_MovesOnlyTheTemporaryFixtureFolderToTheWindowsRecycleBin()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        CreateFile(Path.Combine("RecycleFolder", "Nested", "keep.txt"), "recycle fixture");
        var recyclePath = Path.Combine(_rootPath, "RecycleFolder");
        var service = new WindowsFileCommandService();

        await service.RecycleAsync(new FileCommandTarget(
            _rootPath,
            "RecycleFolder",
            FileCommandItemKind.Folder));

        Assert.False(Directory.Exists(recyclePath));
        Assert.True(Directory.Exists(_rootPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private string CreateFile(string relativePath, string contents)
    {
        var fullPath = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }
}
