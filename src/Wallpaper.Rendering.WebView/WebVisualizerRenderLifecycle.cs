using Wallpaper.Rendering.Abstractions;

namespace Wallpaper.Rendering.WebView;

public sealed class WebVisualizerRenderLifecycle : IWallpaperRenderLifecycle
{
    private readonly object _stateLock = new();
    private WebVisualizerSurface? _surface;
    private RenderLayerState _state = RenderLayerState.Stopped;
    private RenderLayerState _stateBeforeFault = RenderLayerState.Stopped;
    private string _selectedSceneId = "baseline";
    private bool _disposed;

    public RenderLayerState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(RenderLayerState.Running);
        return ValueTask.CompletedTask;
    }

    public void AttachSurface(WebVisualizerSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_surface is not null && !ReferenceEquals(_surface, surface))
            {
                throw new InvalidOperationException("렌더 수명주기는 한 visualizer surface에만 연결할 수 있습니다.");
            }

            _surface = surface;
            surface.InitializationFailed -= Surface_OnInitializationFailed;
            surface.InitializationFailed += Surface_OnInitializationFailed;
            surface.InitializationCompleted -= Surface_OnInitializationCompleted;
            surface.InitializationCompleted += Surface_OnInitializationCompleted;
            surface.SetLifecycleState(_state);
            surface.SelectScene(_selectedSceneId);
        }
    }

    public void SelectScene(string sceneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneId);

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _selectedSceneId = sceneId;
            _surface?.SelectScene(sceneId);
        }
    }

    public void DetachSurface(WebVisualizerSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        lock (_stateLock)
        {
            if (!ReferenceEquals(_surface, surface))
            {
                return;
            }

            surface.InitializationFailed -= Surface_OnInitializationFailed;
            surface.InitializationCompleted -= Surface_OnInitializationCompleted;
            _surface = null;
        }
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            if (_state == RenderLayerState.Running)
            {
                SetStateCore(RenderLayerState.Paused);
            }
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            if (_state == RenderLayerState.Paused)
            {
                SetStateCore(RenderLayerState.Running);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        WebVisualizerSurface? surface;
        lock (_stateLock)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            SetStateCore(RenderLayerState.Stopped);
            surface = _surface;
            _surface = null;
            if (surface is not null)
            {
                surface.InitializationFailed -= Surface_OnInitializationFailed;
                surface.InitializationCompleted -= Surface_OnInitializationCompleted;
            }
        }

        surface?.Dispose();
        return ValueTask.CompletedTask;
    }

    private void SetState(RenderLayerState state)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            SetStateCore(state);
        }
    }

    private void SetStateCore(RenderLayerState state)
    {
        _state = state;
        _surface?.SetLifecycleState(state);
    }

    private void Surface_OnInitializationFailed(object? sender, WebVisualizerFailureEventArgs e)
    {
        lock (_stateLock)
        {
            if (!_disposed)
            {
                if (_state != RenderLayerState.Faulted)
                {
                    _stateBeforeFault = _state;
                }

                _state = RenderLayerState.Faulted;
            }
        }
    }

    private void Surface_OnInitializationCompleted(object? sender, EventArgs e)
    {
        lock (_stateLock)
        {
            if (_disposed || _state != RenderLayerState.Faulted)
            {
                return;
            }

            SetStateCore(_stateBeforeFault);
        }
    }
}
