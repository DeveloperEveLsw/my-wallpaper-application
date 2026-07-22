namespace Wallpaper.Hosts;

internal interface IWallpaperEngineInterop
{
    bool IsWindow(nint windowHandle);

    nint GetParentWindow(nint windowHandle);

    string? GetWindowProcessName(nint windowHandle);

    bool IsWallpaperEngineRunning();

    bool IsParentConnectedToDesktop(nint parentWindowHandle);

    bool IsRenderingVisible(nint windowHandle, nint parentWindowHandle);

    void PlaceInsideParent(nint windowHandle, nint parentWindowHandle);

    void EnsureInteractiveInput(nint parentWindowHandle);

    void RestoreInteractiveInput();
}
