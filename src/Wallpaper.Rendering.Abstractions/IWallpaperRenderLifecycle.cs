namespace Wallpaper.Rendering.Abstractions;

public interface IWallpaperRenderLifecycle : IAsyncDisposable
{
    RenderLayerState State { get; }

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    void Pause();

    void Resume();
}

public enum RenderLayerState
{
    Stopped,
    Running,
    Paused,
    Faulted,
}
