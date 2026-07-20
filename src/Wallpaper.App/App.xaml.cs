using System.Windows;
using Wallpaper.App.Services;
using Wallpaper.App.ViewModels;
using Wallpaper.Core.Scanning;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _renderLifecycle = new PlaceholderRenderLifecycle();
        await _renderLifecycle.StartAsync();

        var settingsDirectory = Environment.GetEnvironmentVariable("WALLPAPER_SETTINGS_DIRECTORY");
        var viewModel = new MainViewModel(
            new ShallowDesktopScanner(),
            new JsonAppSettingsStore(settingsDirectory),
            new WindowsFolderPicker(),
            new DebouncedFileSystemWatcher(),
            new WindowsFileVisualService(),
            new WindowsFileCommandService());
        var window = new MainWindow(viewModel, new WindowsShellContextMenuService());
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_renderLifecycle is not null)
        {
            _renderLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }
}
