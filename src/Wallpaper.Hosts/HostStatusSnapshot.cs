namespace Wallpaper.Hosts;

public sealed record HostStatusSnapshot(
    HostKind Kind,
    HostRuntimeState State,
    nint WindowHandle,
    nint ParentWindowHandle,
    string Message)
{
    public string DisplayText => Kind switch
    {
        HostKind.Standalone => $"Standalone · {Message}",
        HostKind.WallpaperEngine => $"Wallpaper Engine · {Message}",
        _ => Message,
    };
}
