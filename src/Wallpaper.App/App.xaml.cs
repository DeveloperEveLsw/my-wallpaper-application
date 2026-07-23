using System.Windows;
using Wallpaper.App.Services;
using Wallpaper.App.ViewModels;
using Wallpaper.Core.Scanning;
using Wallpaper.Hosts;
using Wallpaper.Infrastructure.Windows.FileOperations;
using Wallpaper.Infrastructure.Windows.Settings;
using Wallpaper.Infrastructure.Windows.Shell;
using Wallpaper.Infrastructure.Windows.Visuals;
using Wallpaper.Infrastructure.Windows.Watching;
using Wallpaper.Rendering.WebView;

namespace Wallpaper.App;

public partial class App : Application
{
    private WebVisualizerRenderLifecycle? _renderLifecycle;
    private WallpaperEngineHost? _wallpaperHost;
    private IWallpaperPresentation? _presentation;
    private WallpaperView? _wallpaperView;
    private MainViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (WallpaperEngineWatchdog.TryParse(e.Args, out var watchdogOptions))
        {
            try
            {
                await WallpaperEngineWatchdog.RunAsync(watchdogOptions!);
            }
            finally
            {
                Shutdown();
            }

            return;
        }

        try
        {
            var launchOptions = HostLaunchOptions.Resolve(e.Args);
            if (!launchOptions.UseDevelopmentWindow)
            {
                WallpaperEngineWatchdog.StartForCurrentProcess(e.Args);
            }

            var visualizerLifecycle = new WebVisualizerRenderLifecycle();
            _renderLifecycle = visualizerLifecycle;
            await visualizerLifecycle.StartAsync();

            var settingsDirectory =
                Environment.GetEnvironmentVariable("WALLPAPER_SETTINGS_DIRECTORY");
            _viewModel = new MainViewModel(
                new ShallowDesktopScanner(),
                new JsonAppSettingsStore(settingsDirectory),
                new WindowsFolderPicker(),
                new DebouncedFileSystemWatcher(),
                new WindowsFileVisualService(),
                new WindowsFileCommandService());

            WallpaperView CreateView(nint inputWindowHandle)
            {
                var view = new WallpaperView(
                    _viewModel,
                    new WindowsShellContextMenuService(),
                    visualizerLifecycle,
                    inputWindowHandle);
                if (!launchOptions.UseDevelopmentWindow)
                {
                    view.Opacity = 0;
                }

                return view;
            }

            if (launchOptions.UseDevelopmentWindow)
            {
                _viewModel.UpdateHostStatus("Development window · Active");
                _presentation = new DevelopmentWindowPresentation(CreateView);
            }
            else
            {
                _wallpaperHost = new WallpaperEngineHost(
                    visualizerLifecycle,
                    launchOptions.ParentWindowHandle);
                _viewModel.UpdateHostStatus(_wallpaperHost.Status.DisplayText);
                _wallpaperHost.StatusChanged += WallpaperHost_OnStatusChanged;
                _wallpaperHost.ExitRequested += WallpaperHost_OnExitRequested;
                _presentation = new WallpaperEnginePresentation(
                    launchOptions.ParentWindowHandle,
                    CreateView);
            }

            _wallpaperView = _presentation.View;
            _presentation.Closed += Presentation_OnClosed;

            _wallpaperHost?.Attach(_presentation.Handle);
            _presentation.Show();
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                PlatformNotSupportedException or
                System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                exception.Message,
                "My Wallpaper Application",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_presentation is not null)
        {
            _presentation.Closed -= Presentation_OnClosed;
        }

        if (_wallpaperHost is not null)
        {
            _wallpaperHost.StatusChanged -= WallpaperHost_OnStatusChanged;
            _wallpaperHost.ExitRequested -= WallpaperHost_OnExitRequested;
            _wallpaperHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_renderLifecycle is not null)
        {
            _renderLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _wallpaperView?.Dispose();
        _presentation?.Dispose();
        _viewModel?.Dispose();

        base.OnExit(e);
    }

    private void WallpaperHost_OnStatusChanged(
        object? sender,
        HostStatusChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => WallpaperHost_OnStatusChanged(sender, e));
            return;
        }

        if (_wallpaperView is null)
        {
            return;
        }

        _wallpaperView.ViewModel.UpdateHostStatus(e.Status.DisplayText);
        _wallpaperView.Opacity =
            e.Status.State is HostRuntimeState.Active or HostRuntimeState.Paused
                ? 1
                : 0;
    }

    private void WallpaperHost_OnExitRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => WallpaperHost_OnExitRequested(sender, e));
            return;
        }

        Shutdown();
    }

    private void Presentation_OnClosed(object? sender, EventArgs e) => Shutdown();
}
