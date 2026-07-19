using Wallpaper.Core.Naming;

namespace Wallpaper.Core.Tests;

public sealed class WindowsFileNameValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("CON.txt")]
    [InlineData("bad?.txt")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void Validate_RejectsNamesWindowsCannotUse(string name)
    {
        Assert.False(WindowsFileNameValidator.Validate(name).IsValid);
    }

    [Theory]
    [InlineData("보고서.txt")]
    [InlineData("photo (1).png")]
    [InlineData("archive.tar.gz")]
    public void Validate_AcceptsOrdinaryNames(string name)
    {
        Assert.True(WindowsFileNameValidator.Validate(name).IsValid);
    }
}
