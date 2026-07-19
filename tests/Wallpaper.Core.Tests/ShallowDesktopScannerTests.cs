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
}
