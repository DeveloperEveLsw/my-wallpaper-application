using Wallpaper.Hosts;

namespace Wallpaper.Hosts.Tests;

public sealed class WallpaperEngineInputRouterStateTests
{
    [Fact]
    public void ShouldApply_AcceptsInitialState()
    {
        var state = new WallpaperEngineInputRouterState();

        Assert.True(state.ShouldApply(enableInput: true, forceReconcile: false));
    }

    [Fact]
    public void ShouldApply_RejectsUnchangedState()
    {
        var state = new WallpaperEngineInputRouterState();
        state.MarkApplied(enableInput: true);

        Assert.False(state.ShouldApply(enableInput: true, forceReconcile: false));
    }

    [Fact]
    public void ShouldApply_AcceptsStateTransition()
    {
        var state = new WallpaperEngineInputRouterState();
        state.MarkApplied(enableInput: true);

        Assert.True(state.ShouldApply(enableInput: false, forceReconcile: false));
    }

    [Fact]
    public void ShouldApply_AcceptsPeriodicReconciliation()
    {
        var state = new WallpaperEngineInputRouterState();
        state.MarkApplied(enableInput: true);

        Assert.True(state.ShouldApply(enableInput: true, forceReconcile: true));
    }

    [Fact]
    public void Reset_ForgetsAppliedAndWindowState()
    {
        var state = new WallpaperEngineInputRouterState
        {
            WorkerWindowHandle = 101,
            DesktopViewWindowHandle = 202,
            InteractiveWorkerRaised = true,
        };
        state.MarkApplied(enableInput: true);
        state.SetTargetRectangle(left: 0, top: 0, right: 1920, bottom: 1080);

        state.Reset();

        Assert.Equal(0, state.WorkerWindowHandle);
        Assert.Equal(0, state.DesktopViewWindowHandle);
        Assert.False(state.InteractiveWorkerRaised);
        Assert.False(state.HasAppliedState);
        Assert.False(state.InputEnabled);
        Assert.False(state.HasTargetRectangle);
    }
}
