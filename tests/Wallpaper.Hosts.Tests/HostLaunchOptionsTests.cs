using Wallpaper.Hosts;

namespace Wallpaper.Hosts.Tests;

public sealed class HostLaunchOptionsTests
{
    [Fact]
    public void Resolve_DefaultsToStandalone()
    {
        var result = HostLaunchOptions.Resolve([], null, "explorer");

        Assert.Equal(HostKind.Standalone, result.Kind);
        Assert.False(result.WasExplicit);
    }

    [Theory]
    [InlineData("wallpaper32")]
    [InlineData("wallpaper64.exe")]
    [InlineData("WALLPAPER64")]
    public void Resolve_DetectsWallpaperEngineParent(string parentProcessName)
    {
        var result = HostLaunchOptions.Resolve([], null, parentProcessName);

        Assert.Equal(HostKind.WallpaperEngine, result.Kind);
        Assert.False(result.WasExplicit);
    }

    [Fact]
    public void Resolve_DetectsWallpaperEngineParentWindowContract()
    {
        var result = HostLaunchOptions.Resolve(
            ["-WINDOWED", "-parentHWND", "12190082"],
            null,
            "wallpaper64");

        Assert.Equal(HostKind.WallpaperEngine, result.Kind);
        Assert.False(result.WasExplicit);
        Assert.Equal((nint)12190082, result.ParentWindowHandle);
    }

    [Fact]
    public void Resolve_ParentWindowContractSelectsWallpaperEngineWithoutProcessHint()
    {
        var result = HostLaunchOptions.Resolve(
            ["-parenthwnd", "4096"],
            null,
            "explorer");

        Assert.Equal(HostKind.WallpaperEngine, result.Kind);
        Assert.Equal((nint)4096, result.ParentWindowHandle);
    }

    [Theory]
    [InlineData("D:\\SteamLibrary\\steamapps\\common\\wallpaper_engine\\projects\\myprojects\\app\\")]
    [InlineData("D:/SteamLibrary/steamapps/common/wallpaper_engine/projects/myprojects/app/")]
    public void Resolve_DetectsWallpaperEngineProjectDirectory(string baseDirectory)
    {
        var result = HostLaunchOptions.Resolve([], null, "explorer", baseDirectory);

        Assert.Equal(HostKind.WallpaperEngine, result.Kind);
        Assert.False(result.WasExplicit);
    }

    [Fact]
    public void Resolve_ArgumentOverridesEnvironmentAndParent()
    {
        var result = HostLaunchOptions.Resolve(
            ["--host=standalone"],
            "wallpaper-engine",
            "wallpaper64");

        Assert.Equal(HostKind.Standalone, result.Kind);
        Assert.True(result.WasExplicit);
        Assert.Equal(0, result.ParentWindowHandle);
    }

    [Theory]
    [InlineData("--host", "wallpaper-engine")]
    [InlineData("--host", "we")]
    [InlineData("--host=wallpaperengine", null)]
    public void Resolve_AcceptsWallpaperEngineAliases(string firstArgument, string? secondArgument)
    {
        var arguments = secondArgument is null
            ? new[] { firstArgument }
            : new[] { firstArgument, secondArgument };

        var result = HostLaunchOptions.Resolve(arguments, null, null);

        Assert.Equal(HostKind.WallpaperEngine, result.Kind);
        Assert.True(result.WasExplicit);
    }

    [Fact]
    public void Resolve_UsesEnvironmentOverride()
    {
        var result = HostLaunchOptions.Resolve([], "wallpaper_engine", null);

        Assert.Equal(HostKind.WallpaperEngine, result.Kind);
        Assert.True(result.WasExplicit);
    }

    [Fact]
    public void Resolve_RejectsMissingHostArgumentValue()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            HostLaunchOptions.Resolve(["--host"], null, null));

        Assert.Contains("호스트 이름", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsUnknownHost()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            HostLaunchOptions.Resolve(["--host=lively"], null, null));

        Assert.Contains("지원하지 않는 호스트", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("not-a-handle")]
    public void Resolve_RejectsInvalidParentWindowHandle(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            HostLaunchOptions.Resolve(["-parentHWND", value], null, null));

        Assert.Contains("parent HWND", exception.Message, StringComparison.Ordinal);
    }
}
