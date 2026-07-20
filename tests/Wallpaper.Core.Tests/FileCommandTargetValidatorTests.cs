using Wallpaper.Core.FileOperations;

namespace Wallpaper.Core.Tests;

public sealed class FileCommandTargetValidatorTests
{
    [Fact]
    public void ValidateExisting_AcceptsRootFileAndDirectChildFolderFile()
    {
        using var fixture = new TemporaryDirectory();
        fixture.CreateFile("root-file.txt");
        fixture.CreateFile(Path.Combine("Work", "report.txt"));

        var rootFile = FileCommandTargetValidator.ValidateExisting(new FileCommandTarget(
            fixture.Path,
            "root-file.txt",
            FileCommandItemKind.File));
        var folderFile = FileCommandTargetValidator.ValidateExisting(new FileCommandTarget(
            fixture.Path,
            "Work/report.txt",
            FileCommandItemKind.File));

        Assert.Equal(Path.Combine(fixture.Path, "root-file.txt"), rootFile.AbsolutePath);
        Assert.Equal(Path.Combine(fixture.Path, "Work", "report.txt"), folderFile.AbsolutePath);
    }

    [Fact]
    public void ValidateExisting_AcceptsDirectChildFolder()
    {
        using var fixture = new TemporaryDirectory();
        fixture.CreateDirectory("Work");

        var result = FileCommandTargetValidator.ValidateExisting(new FileCommandTarget(
            fixture.Path,
            "Work",
            FileCommandItemKind.Folder));

        Assert.Equal(Path.Combine(fixture.Path, "Work"), result.AbsolutePath);
    }

    [Theory]
    [InlineData("../outside.txt", FileCommandItemKind.File)]
    [InlineData("Work/Nested/report.txt", FileCommandItemKind.File)]
    [InlineData("Work/Nested", FileCommandItemKind.Folder)]
    public void ValidateExisting_RejectsOutsideOrUnsupportedDepth(
        string relativePath,
        FileCommandItemKind kind)
    {
        using var fixture = new TemporaryDirectory();

        var exception = Assert.Throws<FileCommandException>(() =>
            FileCommandTargetValidator.ValidateExisting(new FileCommandTarget(
                fixture.Path,
                relativePath,
                kind)));

        Assert.Equal(FileCommandError.InvalidTarget, exception.Error);
    }

    [Fact]
    public void ValidateExisting_RejectsMissingAndKindChangedTargets()
    {
        using var fixture = new TemporaryDirectory();
        fixture.CreateDirectory("Work");

        var missing = Assert.Throws<FileCommandException>(() =>
            FileCommandTargetValidator.ValidateExisting(new FileCommandTarget(
                fixture.Path,
                "missing.txt",
                FileCommandItemKind.File)));
        var wrongKind = Assert.Throws<FileCommandException>(() =>
            FileCommandTargetValidator.ValidateExisting(new FileCommandTarget(
                fixture.Path,
                "Work",
                FileCommandItemKind.File)));

        Assert.Equal(FileCommandError.TargetMissing, missing.Error);
        Assert.Equal(FileCommandError.InvalidTarget, wrongKind.Error);
    }
}
