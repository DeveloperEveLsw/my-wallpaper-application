using System.Windows;
using Wallpaper.App.Services;
using Wallpaper.App.ViewModels;
using Wallpaper.Core.Scanning;
using Wallpaper.Infrastructure.Windows.Settings;
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

        var viewModel = new MainViewModel(
            new ShallowDesktopScanner(),
            new JsonAppSettingsStore(),
            new WindowsFolderPicker());
        var window = new MainWindow(viewModel);
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
