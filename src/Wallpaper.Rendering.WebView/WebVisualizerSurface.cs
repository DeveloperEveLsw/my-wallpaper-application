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
    private readonly WebView2CompositionControl _webView;
    private RenderLayerState _pendingState = RenderLayerState.Stopped;
    private string _pendingSceneId = "baseline";
    private bool _initializationStarted;
    private bool _rendererReady;
    private bool _disposed;

    public WebVisualizerSurface()
    {
        Background = new SolidColorBrush(Color.FromRgb(5, 7, 11));
        ClipToBounds = true;
        IsHitTestVisible = false;

        _webView = new WebView2CompositionControl
        {
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 5, 7, 11),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            Focusable = false,
        };
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
        Loaded -= Surface_OnLoaded;
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_OnWebMessageReceived;
            _webView.CoreWebView2.ProcessFailed -= CoreWebView2_OnProcessFailed;
        }

        Children.Clear();
        _webView.Dispose();
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
            await InitializeAsync();
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Web visualizer initialization failed: {exception}");
            InitializationFailed?.Invoke(this, new WebVisualizerFailureEventArgs(exception));
            return;
        }

        InitializationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private async Task InitializeAsync()
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

        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataDirectory);
        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2;
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
        core.Navigate($"https://{VirtualHostName}/index.html");
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
        var exception = new InvalidOperationException(
            $"Web visualizer process failed: {e.ProcessFailedKind}");
        Trace.TraceError(exception.Message);
        InitializationFailed?.Invoke(this, new WebVisualizerFailureEventArgs(exception));
    }

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
