namespace Wallpaper.Rendering.Abstractions;

public sealed class PlaceholderRenderLifecycle : IWallpaperRenderLifecycle
{
    public RenderLayerState State { get; private set; } = RenderLayerState.Stopped;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = RenderLayerState.Running;
        return ValueTask.CompletedTask;
    }

    public void Pause()
    {
        if (State == RenderLayerState.Running)
        {
            State = RenderLayerState.Paused;
        }
    }

    public void Resume()
    {
        if (State == RenderLayerState.Paused)
        {
            State = RenderLayerState.Running;
        }
    }

    public ValueTask DisposeAsync()
    {
        State = RenderLayerState.Stopped;
        return ValueTask.CompletedTask;
    }
}
