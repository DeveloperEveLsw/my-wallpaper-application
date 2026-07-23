using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wallpaper.Rendering.Abstractions;

namespace Wallpaper.Rendering.WebView;

public sealed class WebVisualizerSurface : Grid, IDisposable
{
    private const string VirtualHostName = "visualizer.wallpaper";
    private static readonly TimeSpan RendererReadyTimeout = TimeSpan.FromSeconds(10);
    private const int MaximumRecoveryAttempts = 5;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private WebView2CompositionControl _webView;
    private RenderLayerState _pendingState = RenderLayerState.Stopped;
    private string _pendingSceneId = "baseline";
    private Task? _recoveryTask;
    private int _navigationGeneration;
    private bool _initializationStarted;
    private bool _rendererReady;
    private bool _disposed;

    public WebVisualizerSurface()
    {
        Background = new SolidColorBrush(Color.FromRgb(5, 7, 11));
        ClipToBounds = true;
        IsHitTestVisible = false;

        _webView = CreateWebView();
        Children.Add(_webView);
        Loaded += Surface_OnLoaded;
    }

    public event EventHandler<WebVisualizerFailureEventArgs>? InitializationFailed;

    public event EventHandler? InitializationCompleted;

    public void SetLifecycleState(RenderLayerState state)
    {
        _pendingState = state;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(SendLifecycleStateIfReady);
            return;
        }

        SendLifecycleStateIfReady();
    }

    public void SelectScene(string sceneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneId);
        _pendingSceneId = sceneId;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(SendSelectedSceneIfReady);
            return;
        }

        SendSelectedSceneIfReady();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(Dispose);
            return;
        }

        _disposed = true;
        _disposeCancellation.Cancel();
        Loaded -= Surface_OnLoaded;
        DisposeWebView(_webView);
        _disposeCancellation.Dispose();
    }

    private async void Surface_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted || _disposed)
        {
            return;
        }

        _initializationStarted = true;
        try
        {
            await InitializeAsync(_webView);
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Web visualizer initialization failed: {exception}");
            ScheduleRecovery(exception);
            return;
        }

        InitializationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private async Task InitializeAsync(WebView2CompositionControl webView)
    {
        var assetDirectory = Path.Combine(AppContext.BaseDirectory, "WebVisualizer");
        var entryPoint = Path.Combine(assetDirectory, "index.html");
        if (!File.Exists(entryPoint))
        {
            throw new IOException($"Web visualizer entry point를 찾을 수 없습니다: {entryPoint}");
        }

        var userDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyWallpaperApplication",
            "WebView2");
        Directory.CreateDirectory(userDataDirectory);

        var environmentOptions = new CoreWebView2EnvironmentOptions
        {
            // Wallpaper Engine can replace the host before the previous WebView2 browser
            // process has fully exited. Do not attach the new host to that stale process.
            ExclusiveUserDataFolderAccess = true,
        };
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataDirectory,
            options: environmentOptions);
        ThrowIfStale(webView);
        await webView.EnsureCoreWebView2Async(environment);
        ThrowIfStale(webView);

        var core = webView.CoreWebView2;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            assetDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.WebMessageReceived += CoreWebView2_OnWebMessageReceived;
        core.ProcessFailed += CoreWebView2_OnProcessFailed;
        _rendererReady = false;
        var navigationGeneration = ++_navigationGeneration;
        core.Navigate($"https://{VirtualHostName}/index.html");
        _ = MonitorRendererReadyAsync(navigationGeneration);
    }

    private void CoreWebView2_OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            if (message.RootElement.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "ready", StringComparison.Ordinal))
            {
                _rendererReady = true;
                SendLifecycleStateIfReady();
                SendSelectedSceneIfReady();
            }
        }
        catch (JsonException exception)
        {
            Trace.TraceWarning($"Web visualizer sent an invalid message: {exception.Message}");
        }
    }

    private void CoreWebView2_OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (!RequiresRecovery(e.ProcessFailedKind))
        {
            Trace.TraceWarning(
                $"Web visualizer process exited and will be recovered by WebView2: " +
                $"{e.ProcessFailedKind} · {e.Reason} · 0x{e.ExitCode:X8}");
            return;
        }

        var exception = new InvalidOperationException(
            $"Web visualizer process failed: {e.ProcessFailedKind} · " +
            $"{e.Reason} · 0x{e.ExitCode:X8}");
        Trace.TraceError(exception.Message);
        ScheduleRecovery(exception);
    }

    private void ScheduleRecovery(Exception exception)
    {
        if (_disposed || _recoveryTask is { IsCompleted: false })
        {
            return;
        }

        _rendererReady = false;
        InitializationFailed?.Invoke(this, new WebVisualizerFailureEventArgs(exception));
        _recoveryTask = RecoverAsync(_disposeCancellation.Token);
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaximumRecoveryAttempts; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                if (_disposed)
                {
                    return;
                }

                ReplaceWebView();
                await InitializeAsync(_webView);
                Trace.TraceInformation(
                    $"Web visualizer recovered on attempt {attempt}.");
                InitializationCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                lastException = exception;
                Trace.TraceWarning(
                    $"Web visualizer recovery attempt {attempt} failed: {exception.Message}");
            }
        }

        if (!_disposed && lastException is not null)
        {
            Trace.TraceError($"Web visualizer recovery failed: {lastException}");
            InitializationFailed?.Invoke(
                this,
                new WebVisualizerFailureEventArgs(lastException));
        }
    }

    private async Task MonitorRendererReadyAsync(int navigationGeneration)
    {
        try
        {
            await Task.Delay(RendererReadyTimeout, _disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (_disposed ||
            _rendererReady ||
            navigationGeneration != _navigationGeneration)
        {
            return;
        }

        var exception = new TimeoutException(
            $"Web visualizer가 {RendererReadyTimeout.TotalSeconds:0}초 안에 준비되지 않았습니다.");
        Trace.TraceError(exception.Message);
        ScheduleRecovery(exception);
    }

    private void ReplaceWebView()
    {
        var previousWebView = _webView;
        DisposeWebView(previousWebView);
        _webView = CreateWebView();
        Children.Add(_webView);
    }

    private void DisposeWebView(WebView2CompositionControl webView)
    {
        if (webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.WebMessageReceived -= CoreWebView2_OnWebMessageReceived;
            webView.CoreWebView2.ProcessFailed -= CoreWebView2_OnProcessFailed;
        }

        Children.Remove(webView);
        webView.Dispose();
    }

    private void ThrowIfStale(WebView2CompositionControl webView)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(_webView, webView))
        {
            throw new OperationCanceledException("WebView2 control이 교체되었습니다.");
        }
    }

    private static WebView2CompositionControl CreateWebView() =>
        new()
        {
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 5, 7, 11),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            Focusable = false,
        };

    private static bool RequiresRecovery(CoreWebView2ProcessFailedKind processFailedKind) =>
        processFailedKind is
            CoreWebView2ProcessFailedKind.BrowserProcessExited or
            CoreWebView2ProcessFailedKind.RenderProcessExited or
            CoreWebView2ProcessFailedKind.RenderProcessUnresponsive or
            CoreWebView2ProcessFailedKind.UnknownProcessExited;

    private void SendLifecycleStateIfReady()
    {
        if (_disposed || !_rendererReady || _webView.CoreWebView2 is null)
        {
            return;
        }

        var state = _pendingState switch
        {
            RenderLayerState.Running => "running",
            RenderLayerState.Paused => "paused",
            RenderLayerState.Faulted => "paused",
            _ => "stopped",
        };
        var json = JsonSerializer.Serialize(new
        {
            type = "lifecycle",
            state,
        });
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void SendSelectedSceneIfReady()
    {
        if (_disposed || !_rendererReady || _webView.CoreWebView2 is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(new
        {
            type = "select-scene",
            sceneId = _pendingSceneId,
        });
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }
}
