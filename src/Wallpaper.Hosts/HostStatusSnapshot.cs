namespace Wallpaper.Hosts;

public sealed record HostStatusSnapshot(
    HostRuntimeState State,
    nint WindowHandle,
    nint ParentWindowHandle,
    string Message)
{
    public string DisplayText => $"Wallpaper Engine · {Message}";
}
