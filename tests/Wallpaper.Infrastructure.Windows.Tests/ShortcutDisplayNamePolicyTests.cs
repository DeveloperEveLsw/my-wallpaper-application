using Wallpaper.Infrastructure.Windows.Visuals;

namespace Wallpaper.Infrastructure.Windows.Tests;

public sealed class ShortcutDisplayNamePolicyTests
{
    [Theory]
    [InlineData("example.lnk", ".lnk", "example")]
    [InlineData("example.exe.lnk", ".lnk", "example.exe")]
    [InlineData("example.tar.gz.lnk", ".lnk", "example.tar.gz")]
    [InlineData("example.url", ".url", "example")]
    [InlineData("example.com.url", ".url", "example.com")]
    [InlineData("report.pdf", ".pdf", "report.pdf")]
    public void CreateFallback_RemovesOnlyTerminalShortcutExtension(
        string fileName,
        string extension,
        string expected)
    {
        Assert.Equal(expected, ShortcutDisplayNamePolicy.CreateFallback(fileName, extension));
    }

    [Theory]
    [InlineData("Example", "example.lnk", ".lnk", "Example")]
    [InlineData("example.exe", "example.exe.lnk", ".lnk", "example.exe")]
    [InlineData("example.exe.lnk", "example.exe.lnk", ".lnk", "example.exe")]
    [InlineData(null, "example.com.url", ".url", "example.com")]
    public void NormalizeShellDisplayName_PreservesMeaningfulPrecedingExtensions(
        string? shellDisplayName,
        string fallbackFileName,
        string extension,
        string expected)
    {
        Assert.Equal(
            expected,
            ShortcutDisplayNamePolicy.NormalizeShellDisplayName(
                shellDisplayName,
                fallbackFileName,
                extension));
    }
}
