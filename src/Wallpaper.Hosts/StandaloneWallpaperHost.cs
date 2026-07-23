namespace Wallpaper.Hosts;

public sealed class StandaloneWallpaperHost : IWallpaperHost
{
    public HostKind Kind => HostKind.Standalone;

    public HostStatusSnapshot Status { get; private set; } = new(
        HostKind.Standalone,
        HostRuntimeState.Starting,
        0,
        0,
        "Starting");

    public event EventHandler<HostStatusChangedEventArgs>? StatusChanged;

    public event EventHandler? ExitRequested
    {
        add { }
        remove { }
    }

    public void Attach(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException("Standalone 창 HWND가 필요합니다.", nameof(windowHandle));
        }

        Status = new HostStatusSnapshot(
            HostKind.Standalone,
            HostRuntimeState.Active,
            windowHandle,
            0,
            "Active");
        StatusChanged?.Invoke(this, new HostStatusChangedEventArgs(Status));
    }

    public void NotifyRenderSurfaceReady()
    {
    }

    public ValueTask DisposeAsync()
    {
        Status = Status with
        {
            State = HostRuntimeState.Stopped,
            Message = "Stopped",
        };
        return ValueTask.CompletedTask;
    }
}
