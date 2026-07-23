using System.Windows;
using System.Windows.Interop;

namespace Wallpaper.App;

internal sealed class DevelopmentWindowPresentation : IWallpaperPresentation
{
    private readonly Window _window;
    private bool _disposed;

    public DevelopmentWindowPresentation(Func<nint, WallpaperView> createView)
    {
        ArgumentNullException.ThrowIfNull(createView);

        _window = new Window
        {
            Title = "My Wallpaper Application · Development",
            Width = 1440,
            Height = 900,
            MinWidth = 1024,
            MinHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized,
            Background = System.Windows.Media.Brushes.Black,
            Foreground = System.Windows.Media.Brushes.White,
        };

        try
        {
            Handle = new WindowInteropHelper(_window).EnsureHandle();
            View = createView(Handle);
            _window.Content = View;
            _window.DpiChanged += Window_OnDpiChanged;
            _window.Closed += Window_OnClosed;
        }
        catch
        {
            _window.Close();
            throw;
        }
    }

    public nint Handle { get; }

    public WallpaperView View { get; }

    public event EventHandler? Closed;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Show();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.DpiChanged -= Window_OnDpiChanged;
        _window.Closed -= Window_OnClosed;
        _window.Content = null;
        if (_window.IsVisible)
        {
            _window.Close();
        }
    }

    private void Window_OnDpiChanged(object sender, DpiChangedEventArgs e) =>
        _ = View.ReloadFileVisualsForDpiAsync();

    private void Window_OnClosed(object? sender, EventArgs e) => Closed?.Invoke(this, EventArgs.Empty);
}
