namespace Wallpaper.Seelen.M0.Companion;

internal static class ProtocolMessages
{
    public const int Version = 1;
    public const int MaximumMessageBytes = 4096;
}

internal sealed record HelloAcknowledgement(
    string Type,
    int Protocol,
    string SessionToken,
    string DesktopRoot,
    string HttpBaseUrl);

internal sealed record PongMessage(string Type, long Timestamp);

internal sealed record ErrorMessage(string Type, string Code);
