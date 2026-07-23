namespace Wallpaper.Seelen.Bootstrap;

internal sealed class HostPolicy(int port)
{
    public string AllowedHost { get; } =
        $"127.0.0.1:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public bool IsAllowed(string? host)
    {
        return string.Equals(host, AllowedHost, StringComparison.Ordinal);
    }
}
