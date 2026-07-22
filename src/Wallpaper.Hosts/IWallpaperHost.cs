namespace Wallpaper.Hosts;

public interface IWallpaperHost : IAsyncDisposable
{
    HostKind Kind { get; }

    HostStatusSnapshot Status { get; }

    event EventHandler<HostStatusChangedEventArgs>? StatusChanged;

    event EventHandler? ExitRequested;

    void Attach(nint windowHandle);
}
