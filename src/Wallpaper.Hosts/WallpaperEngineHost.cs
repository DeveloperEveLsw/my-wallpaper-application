using Wallpaper.Rendering.Abstractions;

namespace Wallpaper.Hosts;

public sealed class WallpaperEngineHost : IWallpaperHost
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HostExitGracePeriod = TimeSpan.FromSeconds(5);

    private readonly object _statusLock = new();
    private readonly IWallpaperRenderLifecycle _renderLifecycle;
    private readonly IWallpaperEngineInterop _interop;
    private readonly TimeProvider _timeProvider;
    private readonly bool _requireWindows;
    private readonly nint _launchParentWindowHandle;
    private Timer? _timer;
    private SynchronizationContext? _synchronizationContext;
    private nint _windowHandle;
    private DateTimeOffset? _hostMissingSince;
    private bool _hadParent;
    private bool _exitRequested;
    private bool _disposed;
    private int _polling;

    public WallpaperEngineHost(
        IWallpaperRenderLifecycle renderLifecycle,
        nint launchParentWindowHandle = 0)
        : this(
            renderLifecycle,
            OperatingSystem.IsWindows()
                ? new WindowsWallpaperEngineInterop()
                : throw new PlatformNotSupportedException(
                    "Wallpaper Engine 호스트는 Windows에서만 사용할 수 있습니다."),
            TimeProvider.System,
            requireWindows: true,
            launchParentWindowHandle)
    {
    }

    internal WallpaperEngineHost(
        IWallpaperRenderLifecycle renderLifecycle,
        IWallpaperEngineInterop interop,
        TimeProvider timeProvider,
        bool requireWindows,
        nint launchParentWindowHandle = 0)
    {
        _renderLifecycle = renderLifecycle ?? throw new ArgumentNullException(nameof(renderLifecycle));
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _requireWindows = requireWindows;
        _launchParentWindowHandle = launchParentWindowHandle;
    }

    public HostKind Kind => HostKind.WallpaperEngine;

    public HostStatusSnapshot Status { get; private set; } = new(
        HostKind.WallpaperEngine,
        HostRuntimeState.Starting,
        0,
        0,
        "Starting");

    public event EventHandler<HostStatusChangedEventArgs>? StatusChanged;

    public event EventHandler? ExitRequested;

    public void Attach(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_requireWindows && !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Wallpaper Engine 호스트는 Windows에서만 사용할 수 있습니다.");
        }

        if (windowHandle == 0 || !_interop.IsWindow(windowHandle))
        {
            throw new ArgumentException("유효한 WPF 창 HWND가 필요합니다.", nameof(windowHandle));
        }

        if (_windowHandle != 0)
        {
            throw new InvalidOperationException("Wallpaper Engine 호스트는 한 창에만 연결할 수 있습니다.");
        }

        _windowHandle = windowHandle;
        _synchronizationContext = SynchronizationContext.Current;
        PublishStatus(new HostStatusSnapshot(
            HostKind.WallpaperEngine,
            HostRuntimeState.WaitingForParent,
            windowHandle,
            0,
            "Waiting for parent HWND"));

        Poll();
        _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var timer = Interlocked.Exchange(ref _timer, null);
        if (timer is not null)
        {
            await timer.DisposeAsync();
        }

        _interop.RestoreInteractiveInput();
        PublishStatus(Status with
        {
            State = HostRuntimeState.Stopped,
            Message = "Stopped",
        });
    }

    internal void Poll()
    {
        if (_disposed || _windowHandle == 0 || Interlocked.Exchange(ref _polling, 1) != 0)
        {
            return;
        }

        try
        {
            EvaluateWindowState();
        }
        catch (Exception exception)
        {
            _renderLifecycle.Pause();
            PublishStatus(new HostStatusSnapshot(
                HostKind.WallpaperEngine,
                HostRuntimeState.Faulted,
                _windowHandle,
                0,
                $"Host error · {exception.Message}"));
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private void EvaluateWindowState()
    {
        if (!_interop.IsWindow(_windowHandle))
        {
            RequestExit();
            return;
        }

        var parentWindow = ResolveParentWindow();
        if (parentWindow == 0 ||
            !_interop.IsWindow(parentWindow) ||
            !_interop.IsParentConnectedToDesktop(parentWindow))
        {
            _renderLifecycle.Pause();
            var state = _hadParent
                ? HostRuntimeState.Recovering
                : HostRuntimeState.WaitingForParent;
            PublishStatus(new HostStatusSnapshot(
                HostKind.WallpaperEngine,
                state,
                _windowHandle,
                0,
                _hadParent ? "Recovering parent HWND" : "Waiting for parent HWND"));
            CheckForHostExit();
            return;
        }

        _hadParent = true;
        _hostMissingSince = null;
        _interop.PlaceInsideParent(_windowHandle, parentWindow);
        _interop.EnsureInteractiveInput(parentWindow);

        var parentProcessName = _interop.GetWindowProcessName(parentWindow) ?? "unknown";
        var isVisible = _interop.IsRenderingVisible(_windowHandle, parentWindow);
        if (isVisible)
        {
            _renderLifecycle.Resume();
        }
        else
        {
            _renderLifecycle.Pause();
        }

        PublishStatus(new HostStatusSnapshot(
            HostKind.WallpaperEngine,
            isVisible ? HostRuntimeState.Active : HostRuntimeState.Paused,
            _windowHandle,
            parentWindow,
            $"{(isVisible ? "Active" : "Paused")} · parent {parentProcessName} " +
            $"0x{parentWindow.ToInt64():X}"));
    }

    private nint ResolveParentWindow()
    {
        if (_launchParentWindowHandle != 0 &&
            _interop.IsWindow(_launchParentWindowHandle))
        {
            return _launchParentWindowHandle;
        }

        return _interop.GetParentWindow(_windowHandle);
    }

    private void CheckForHostExit()
    {
        if (_interop.IsWallpaperEngineRunning())
        {
            _hostMissingSince = null;
            return;
        }

        var now = _timeProvider.GetUtcNow();
        _hostMissingSince ??= now;
        if (now - _hostMissingSince >= HostExitGracePeriod)
        {
            RequestExit();
        }
    }

    private void RequestExit()
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        PostToOwner(() => ExitRequested?.Invoke(this, EventArgs.Empty));
    }

    private void PublishStatus(HostStatusSnapshot status)
    {
        lock (_statusLock)
        {
            if (Status == status)
            {
                return;
            }

            Status = status;
        }

        PostToOwner(() => StatusChanged?.Invoke(this, new HostStatusChangedEventArgs(status)));
    }

    private void PostToOwner(Action action)
    {
        var context = _synchronizationContext;
        if (context is null || context == SynchronizationContext.Current)
        {
            action();
            return;
        }

        context.Post(_ => action(), null);
    }
}
