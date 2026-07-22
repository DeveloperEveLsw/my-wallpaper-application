namespace Wallpaper.Hosts;

public enum HostRuntimeState
{
    Starting,
    WaitingForParent,
    Active,
    Paused,
    Recovering,
    Stopped,
    Faulted,
}
