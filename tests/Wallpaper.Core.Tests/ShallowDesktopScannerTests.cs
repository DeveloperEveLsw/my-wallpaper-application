using Wallpaper.Core.Scanning;

namespace Wallpaper.Core.Tests;

public sealed class ShallowDesktopScannerTests
{
    [Fact]
    public void Scan_ProjectsRootFoldersAndLooseFilesIntoSeparateCollections()
    {
        using var fixture = new TemporaryDirectory();
        fixture.CreateDirectory("Folder2");
        fixture.CreateDirectory("Folder10");
        fixture.CreateFile("Folder2/report.txt");
        fixture.CreateFile("Folder2/Nested/ignored.txt");
        fixture.CreateFile("root.png", "image");

        var snapshot = new ShallowDesktopScanner().Scan(fixture.Path);

        Assert.Equal(["Folder2", "Folder10"], snapshot.Folders.Select(folder => folder.Name));
        Assert.Equal(["report.txt"], snapshot.Folders[0].Files.Select(file => file.Name));
        Assert.Equal(["root.png"], snapshot.RootFiles.Select(file => file.Name));
        Assert.Empty(snapshot.Warnings);
        Assert.All(snapshot.Folders, folder => Assert.DoesNotContain(fixture.Path, folder.RelativePath));
    }

    [Fact]
    public void Scan_UsesStableRelativeIdsWithoutExposingAbsolutePaths()
    {
        using var fixture = new TemporaryDirectory();
        fixture.CreateFile("Work/notes.txt");

        var snapshot = new ShallowDesktopScanner().Scan(fixture.Path);

        var folder = Assert.Single(snapshot.Folders);
        var file = Assert.Single(folder.Files);
        Assert.Equal("folder:WORK", folder.Id);
        Assert.Equal("file:WORK/NOTES.TXT", file.Id);
        Assert.DoesNotContain(fixture.Path, folder.Id);
        Assert.DoesNotContain(fixture.Path, file.Id);
    }

    [Fact]
    public void Scan_RejectsRelativeAndMissingRoots()
    {
        var scanner = new ShallowDesktopScanner();

        var relativeError = Assert.Throws<RootScanException>(() => scanner.Scan("relative/path"));
        var missingError = Assert.Throws<RootScanException>(() =>
            scanner.Scan(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        Assert.Equal(RootScanError.PathNotFullyQualified, relativeError.Error);
        Assert.Equal(RootScanError.DirectoryNotFound, missingError.Error);
    }

    [Fact]
    public void Scan_ReturnsAnEmptySnapshotForAnEmptyRoot()
    {
        using var fixture = new TemporaryDirectory();

        var snapshot = new ShallowDesktopScanner().Scan(fixture.Path);

        Assert.Empty(snapshot.Folders);
        Assert.Empty(snapshot.RootFiles);
        Assert.Empty(snapshot.Warnings);
    }

    [Fact]
    public void Scan_PreservesAnEmptyFolderAsADockCard()
    {
        using var fixture = new TemporaryDirectory();
        fixture.CreateDirectory("Empty");

        var snapshot = new ShallowDesktopScanner().Scan(fixture.Path);

        var folder = Assert.Single(snapshot.Folders);
        Assert.Equal("Empty", folder.Name);
        Assert.Empty(folder.Files);
    }

    [Fact]
    public void Scan_CanReadMetadataForAFileLockedAgainstContentAccess()
    {
        using var fixture = new TemporaryDirectory();
        var path = fixture.CreateFile("locked.png", "not an image");
        using var lockedFile = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var snapshot = new ShallowDesktopScanner().Scan(fixture.Path);

        var file = Assert.Single(snapshot.RootFiles);
        Assert.Equal("locked.png", file.Name);
        Assert.Equal(12, file.Length);
    }

    [Fact]
    public void Scan_HandlesManyFilesWithoutChangingNaturalOrder()
    {
        using var fixture = new TemporaryDirectory();
        for (var index = 1; index <= 1_500; index++)
        {
            fixture.CreateFile($"File{index}.txt");
        }

        var snapshot = new ShallowDesktopScanner().Scan(fixture.Path);

        Assert.Equal(1_500, snapshot.RootFiles.Count);
        Assert.Equal("File1.txt", snapshot.RootFiles[0].Name);
        Assert.Equal("File10.txt", snapshot.RootFiles[9].Name);
        Assert.Equal("File1500.txt", snapshot.RootFiles[^1].Name);
    }
}
