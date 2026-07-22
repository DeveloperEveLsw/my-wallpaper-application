using Wallpaper.Hosts;

namespace Wallpaper.Hosts.Tests;

public sealed class WallpaperEngineInputScopeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1919, 1079)]
    [InlineData(960, 540)]
    public void Contains_AcceptsPointsInsideTargetMonitor(int pointX, int pointY)
    {
        var result = WallpaperEngineInputScope.Contains(
            pointX,
            pointY,
            left: 0,
            top: 0,
            right: 1920,
            bottom: 1080);

        Assert.True(result);
    }

    [Theory]
    [InlineData(-1, 540)]
    [InlineData(-1920, 540)]
    [InlineData(1920, 540)]
    [InlineData(960, -1)]
    [InlineData(960, 1080)]
    public void Contains_RejectsPointsOnOtherMonitors(int pointX, int pointY)
    {
        var result = WallpaperEngineInputScope.Contains(
            pointX,
            pointY,
            left: 0,
            top: 0,
            right: 1920,
            bottom: 1080);

        Assert.False(result);
    }

    [Fact]
    public void Contains_SupportsTargetMonitorWithNegativeCoordinates()
    {
        var result = WallpaperEngineInputScope.Contains(
            pointX: -960,
            pointY: 540,
            left: -1920,
            top: 0,
            right: 0,
            bottom: 1080);

        Assert.True(result);
    }
}
