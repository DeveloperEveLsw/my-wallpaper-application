namespace Wallpaper.Hosts;

public sealed class HostStatusChangedEventArgs(HostStatusSnapshot status) : EventArgs
{
    public HostStatusSnapshot Status { get; } = status;
}
