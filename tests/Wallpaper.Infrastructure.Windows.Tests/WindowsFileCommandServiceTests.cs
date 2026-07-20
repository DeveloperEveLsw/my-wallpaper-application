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
