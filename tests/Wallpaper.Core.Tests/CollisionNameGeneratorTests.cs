using Wallpaper.Core.Naming;

namespace Wallpaper.Core.Tests;

public sealed class CollisionNameGeneratorTests
{
    [Fact]
    public void ProposeAvailableFileName_UsesSmallestAvailableSuffixAndPreservesExtension()
    {
        string[] existing = ["photo.png", "photo (1).png", "photo (3).png"];

        var result = CollisionNameGenerator.ProposeAvailableFileName("photo.png", existing);

        Assert.Equal("photo (2).png", result);
    }

    [Fact]
    public void ProposeAvailableFileName_ReturnsOriginalWhenThereIsNoCollision()
    {
        var result = CollisionNameGenerator.ProposeAvailableFileName("notes.txt", ["other.txt"]);

        Assert.Equal("notes.txt", result);
    }

    [Fact]
    public void ProposeAvailableFileName_TreatsNamesAsCaseInsensitive()
    {
        var result = CollisionNameGenerator.ProposeAvailableFileName(
            "Report.TXT",
            ["report.txt", "REPORT (1).TXT"]);

        Assert.Equal("Report (2).TXT", result);
    }

    [Theory]
    [InlineData("LICENSE", "LICENSE (1)")]
    [InlineData("archive.tar.gz", "archive.tar (1).gz")]
    [InlineData(".env", ".env (1)")]
    public void ProposeAvailableFileName_HandlesExtensionVariants(string name, string expected)
    {
        var result = CollisionNameGenerator.ProposeAvailableFileName(name, [name]);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProposeAvailableFileName_TruncatesLongBaseToKeepAValidComponentLength()
    {
        var name = $"{new string('a', 251)}.txt";

        var result = CollisionNameGenerator.ProposeAvailableFileName(name, [name]);

        Assert.Equal(255, result.Length);
        Assert.EndsWith(" (1).txt", result);
        Assert.True(WindowsFileNameValidator.Validate(result).IsValid);
    }

    [Fact]
    public void ProposeAvailableFileName_RejectsExtensionThatLeavesNoBaseForSuffix()
    {
        var name = $"a.{new string('x', 253)}";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CollisionNameGenerator.ProposeAvailableFileName(name, [name]));

        Assert.Contains("확장자", exception.Message);
    }
}
