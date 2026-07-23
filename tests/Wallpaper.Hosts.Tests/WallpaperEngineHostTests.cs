using Wallpaper.Hosts;
using Wallpaper.Rendering.Abstractions;

namespace Wallpaper.Hosts.Tests;

public sealed class WallpaperEngineHostTests
{
    [Fact]
    public async Task Attach_UsesParentAndResumesVisibleRenderer()
    {
        var lifecycle = new FakeRenderLifecycle();
        await lifecycle.StartAsync();
        var interop = new FakeWallpaperEngineInterop
        {
            ParentWindow = 200,
            RenderingVisible = true,
        };
        await using var host = CreateHost(lifecycle, interop);

        host.Attach(100);

        Assert.Equal(HostRuntimeState.Active, host.Status.State);
        Assert.Equal((nint)200, host.Status.ParentWindowHandle);
        Assert.Equal(RenderLayerState.Running, lifecycle.State);
        Assert.Equal(1, interop.FitCount);
        Assert.Equal(1, interop.InputInitializationCount);
    }

    [Fact]
    public async Task Attach_ReportsFaultWhenWindowWasNotCreatedUnderLaunchParent()
    {
        var lifecycle = new FakeRenderLifecycle();
        await lifecycle.StartAsync();
        var interop = new FakeWallpaperEngineInterop
        {
            ParentWindow = 0,
            RenderingVisible = true,
        };
        await using var host = CreateHost(lifecycle, interop, launchParentWindowHandle: 300);

        host.Attach(100);

        Assert.Equal(HostRuntimeState.Faulted, host.Status.State);
        Assert.Equal(RenderLayerState.Paused, lifecycle.State);
        Assert.Equal(0, interop.InputInitializationCount);
    }

    [Fact]
    public async Task Poll_PausesAndResumesWithWallpaperVisibility()
    {
        var lifecycle = new FakeRenderLifecycle();
        await lifecycle.StartAsync();
        var interop = new FakeWallpaperEngineInterop
        {
            ParentWindow = 200,
            RenderingVisible = true,
        };
        await using var host = CreateHost(lifecycle, interop);
        host.Attach(100);

        interop.RenderingVisible = false;
        host.Poll();

        Assert.Equal(HostRuntimeState.Paused, host.Status.State);
        Assert.Equal(RenderLayerState.Paused, lifecycle.State);

        interop.RenderingVisible = true;
        host.Poll();

        Assert.Equal(HostRuntimeState.Active, host.Status.State);
        Assert.Equal(RenderLayerState.Running, lifecycle.State);
    }

    [Fact]
    public async Task Poll_DoesNotRepeatWindowMutationWhenParentIsUnchanged()
    {
        var lifecycle = new FakeRenderLifecycle();
        await lifecycle.StartAsync();
        var interop = new FakeWallpaperEngineInterop
        {
            ParentWindow = 200,
            RenderingVisible = true,
        };
        await using var host = CreateHost(lifecycle, interop);
        host.Attach(100);

        host.Poll();
        host.Poll();

        Assert.Equal(1, interop.FitCount);
        Assert.Equal(1, interop.InputInitializationCount);
    }

    [Fact]
    public async Task Poll_RecoversWhenLaunchParentBecomesAvailableAgain()
    {
        var lifecycle = new FakeRenderLifecycle();
        await lifecycle.StartAsync();
        var interop = new FakeWallpaperEngineInterop
        {
            ParentWindow = 200,
            RenderingVisible = true,
        };
        await using var host = CreateHost(lifecycle, interop);
        host.Attach(100);

        interop.MissingWindowHandles.Add(200);
        host.Poll();

        Assert.Equal(HostRuntimeState.Recovering, host.Status.State);
        Assert.Equal(RenderLayerState.Paused, lifecycle.State);

        interop.MissingWindowHandles.Remove(200);
        host.Poll();

        Assert.Equal(HostRuntimeState.Active, host.Status.State);
        Assert.Equal((nint)200, host.Status.ParentWindowHandle);
        Assert.Equal(RenderLayerState.Running, lifecycle.State);
        Assert.Equal(2, interop.FitCount);
    }

    [Fact]
    public async Task Poll_ReportsRecoveringWhenWallpaperEngineParentLostDesktopConnection()
    {
        var lifecycle = new FakeRenderLifecycle();
        await lifecycle.StartAsync();
        var interop = new FakeWallpaperEngineInterop
        {
            ParentWindow = 200,
            RenderingVisible = true,
        };
        await using var host = CreateHost(lifecycle, interop);
        host.Attach(100);

        interop.ParentConnectedToDesktop = false;
        host.Poll();

        Assert.Equal(HostRuntimeState.Recovering, host.Status.State);
        Assert.Equal(RenderLayerState.Paused, lifecycle.State);
    }

    [Fact]
    public async Task Poll_RequestsExitWhenWindowWasDestroyed()
    {
        var lifecycle = new FakeRenderLifecycle();
        await lifecycle.StartAsync();
        var interop = new FakeWallpaperEngineInterop
        {
            ParentWindow = 200,
            RenderingVisible = true,
        };
        await using var host = CreateHost(lifecycle, interop);
        var exitRequested = false;
        host.ExitRequested += (_, _) => exitRequested = true;
        host.Attach(100);

        interop.WindowExists = false;
        host.Poll();

        Assert.True(exitRequested);
    }

    [Fact]
    public async Task Dispose_RestoresInteractiveDesktopInput()
    {
        var lifecycle = new FakeRenderLifecycle();
        await lifecycle.StartAsync();
        var interop = new FakeWallpaperEngineInterop
        {
            ParentWindow = 200,
            RenderingVisible = true,
        };
        var host = CreateHost(lifecycle, interop);
        host.Attach(100);

        await host.DisposeAsync();

        Assert.Equal(1, interop.InputRestoreCount);
    }

    private static WallpaperEngineHost CreateHost(
        IWallpaperRenderLifecycle lifecycle,
        IWallpaperEngineInterop interop,
        nint launchParentWindowHandle = 200) =>
        new(
            lifecycle,
            interop,
            TimeProvider.System,
            requireWindows: false,
            launchParentWindowHandle);

    private sealed class FakeRenderLifecycle : IWallpaperRenderLifecycle
    {
        public RenderLayerState State { get; private set; } = RenderLayerState.Stopped;

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            State = RenderLayerState.Running;
            return ValueTask.CompletedTask;
        }

        public void Pause() => State = RenderLayerState.Paused;

        public void Resume() => State = RenderLayerState.Running;

        public ValueTask DisposeAsync()
        {
            State = RenderLayerState.Stopped;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeWallpaperEngineInterop : IWallpaperEngineInterop
    {
        public bool WindowExists { get; set; } = true;

        public nint ParentWindow { get; set; }

        public bool RenderingVisible { get; set; }

        public bool WallpaperEngineRunning { get; set; } = true;

        public bool ParentConnectedToDesktop { get; set; } = true;

        public HashSet<nint> MissingWindowHandles { get; } = [];

        public int FitCount { get; private set; }

        public int InputInitializationCount { get; private set; }

        public int InputRestoreCount { get; private set; }

        public bool IsWindow(nint windowHandle) =>
            WindowExists &&
            windowHandle != 0 &&
            !MissingWindowHandles.Contains(windowHandle);

        public string? GetWindowProcessName(nint windowHandle) => "wallpaper64";

        public bool IsWallpaperEngineRunning() => WallpaperEngineRunning;

        public bool IsParentConnectedToDesktop(nint parentWindowHandle) =>
            ParentConnectedToDesktop;

        public bool IsRenderingVisible(nint windowHandle, nint parentWindowHandle) =>
            RenderingVisible;

        public void FitInsideParent(nint windowHandle, nint parentWindowHandle)
        {
            if (ParentWindow != parentWindowHandle)
            {
                throw new InvalidOperationException(
                    "Window was not created under the launch parent.");
            }

            FitCount++;
        }

        public void InitializeInteractiveInput(nint parentWindowHandle) =>
            InputInitializationCount++;

        public void RestoreInteractiveInput() => InputRestoreCount++;
    }
}
