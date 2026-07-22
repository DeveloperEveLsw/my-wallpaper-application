using Wallpaper.Hosts;

namespace Wallpaper.Hosts.Tests;

public sealed class WallpaperEngineWatchdogTests
{
    [Fact]
    public void TryParse_ReturnsFalseForNormalWallpaperEngineLaunch()
    {
        var result = WallpaperEngineWatchdog.TryParse(
            ["-WINDOWED", "-parentHWND", "1024"],
            out var options);

        Assert.False(result);
        Assert.Null(options);
    }

    [Fact]
    public void TryParse_ReadsWatchdogProcessIds()
    {
        var result = WallpaperEngineWatchdog.TryParse(
            [
                "--wallpaper-engine-watchdog",
                "--application-process-id", "101",
                "--engine-process-id", "202",
                "--parent-window-handle", "303",
                "--worker-window-handle", "404",
            ],
            out var options);

        Assert.True(result);
        Assert.Equal(101, options!.ApplicationProcessId);
        Assert.Equal(202, options.EngineProcessId);
        Assert.Equal((nint)303, options.ParentWindowHandle);
        Assert.Equal((nint)404, options.WorkerWindowHandle);
    }

    [Fact]
    public void TryParse_RejectsMissingProcessId()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            WallpaperEngineWatchdog.TryParse(
                [
                    "--wallpaper-engine-watchdog",
                    "--application-process-id", "101",
                ],
                out _));

        Assert.Contains("--engine-process-id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_RejectsInvalidParentWindowHandle()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            WallpaperEngineWatchdog.TryParse(
                [
                    "--wallpaper-engine-watchdog",
                    "--application-process-id", "101",
                    "--engine-process-id", "202",
                    "--parent-window-handle", "0",
                    "--worker-window-handle", "404",
                ],
                out _));

        Assert.Contains("--parent-window-handle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_RejectsMissingWorkerWindowHandle()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            WallpaperEngineWatchdog.TryParse(
                [
                    "--wallpaper-engine-watchdog",
                    "--application-process-id", "101",
                    "--engine-process-id", "202",
                    "--parent-window-handle", "303",
                ],
                out _));

        Assert.Contains("--worker-window-handle", exception.Message, StringComparison.Ordinal);
    }
}
