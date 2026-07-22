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
using Wallpaper.Rendering.Abstractions;

namespace Wallpaper.App;

public partial class App : Application
{
    private IWallpaperRenderLifecycle? _renderLifecycle;
    private IWallpaperHost? _wallpaperHost;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (WallpaperEngineWatchdog.TryParse(e.Args, out var watchdogOptions))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
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

        _renderLifecycle = new PlaceholderRenderLifecycle();
        await _renderLifecycle.StartAsync();

        try
        {
            _wallpaperHost = WallpaperHostFactory.Create(_renderLifecycle, e.Args);
            if (_wallpaperHost.Kind == HostKind.WallpaperEngine)
            {
                WallpaperEngineWatchdog.StartForCurrentProcess(e.Args);
            }
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
            return;
        }

        var settingsDirectory = Environment.GetEnvironmentVariable("WALLPAPER_SETTINGS_DIRECTORY");
        var viewModel = new MainViewModel(
            new ShallowDesktopScanner(),
            new JsonAppSettingsStore(settingsDirectory),
            new WindowsFolderPicker(),
            new DebouncedFileSystemWatcher(),
            new WindowsFileVisualService(),
            new WindowsFileCommandService());
        viewModel.UpdateHostStatus(_wallpaperHost.Status.DisplayText);
        var window = new MainWindow(
            viewModel,
            new WindowsShellContextMenuService(),
            _wallpaperHost);
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_wallpaperHost is not null)
        {
            _wallpaperHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_renderLifecycle is not null)
        {
            _renderLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }
}
