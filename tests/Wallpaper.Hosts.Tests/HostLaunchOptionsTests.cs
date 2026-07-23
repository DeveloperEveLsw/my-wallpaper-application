using Wallpaper.Hosts;

namespace Wallpaper.Hosts.Tests;

public sealed class HostLaunchOptionsTests
{
    [Fact]
    public void Resolve_AcceptsWallpaperEngineParentWindowContract()
    {
        var result = HostLaunchOptions.Resolve(
            ["-WINDOWED", "-parentHWND", "12190082"]);

        Assert.False(result.UseDevelopmentWindow);
        Assert.Equal((nint)12190082, result.ParentWindowHandle);
    }

    [Fact]
    public void Resolve_AcceptsLongParentWindowArgument()
    {
        var result = HostLaunchOptions.Resolve(["--parent-hwnd=4096"]);

        Assert.False(result.UseDevelopmentWindow);
        Assert.Equal((nint)4096, result.ParentWindowHandle);
    }

    [Fact]
    public void Resolve_AcceptsExplicitDevelopmentWindow()
    {
        var result = HostLaunchOptions.Resolve(["--dev-window"]);

        Assert.True(result.UseDevelopmentWindow);
        Assert.Equal(0, result.ParentWindowHandle);
    }

    [Fact]
    public void Resolve_RejectsImplicitStandaloneLaunch()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            HostLaunchOptions.Resolve([]));

        Assert.Contains("-parentHWND", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--dev-window", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsDevelopmentWindowWithParentHandle()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            HostLaunchOptions.Resolve(["--dev-window", "-parentHWND", "4096"]));

        Assert.Contains("함께 사용할 수 없습니다", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("not-a-handle")]
    public void Resolve_RejectsInvalidParentWindowHandle(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            HostLaunchOptions.Resolve(["-parentHWND", value]));

        Assert.Contains("parent HWND", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsMissingParentWindowValue()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            HostLaunchOptions.Resolve(["-parentHWND"]));

        Assert.Contains("HWND 값", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--host=standalone")]
    [InlineData("--host")]
    public void Resolve_RejectsRemovedHostModeOption(string argument)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            HostLaunchOptions.Resolve([argument]));

        Assert.Contains("--host 옵션은 제거", exception.Message, StringComparison.Ordinal);
    }
}
