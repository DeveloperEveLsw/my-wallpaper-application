namespace Wallpaper.App;

internal interface IWallpaperPresentation : IDisposable
{
    nint Handle { get; }

    WallpaperView View { get; }

    event EventHandler? Closed;

    void Show();
}
