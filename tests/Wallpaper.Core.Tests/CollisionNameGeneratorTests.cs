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
}
