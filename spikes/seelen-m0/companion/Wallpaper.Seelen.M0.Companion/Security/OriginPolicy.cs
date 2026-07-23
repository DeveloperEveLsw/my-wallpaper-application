namespace Wallpaper.Seelen.M0.Companion;

internal static class OriginPolicy
{
    // Tauri v2 WebviewUrl.App uses this origin in WebView2 on Windows. M0 must
    // fail closed if the installed Seelen build reports a different origin.
    public const string SeelenAppOrigin = "http://tauri.localhost";

    public static bool IsAllowed(string? origin)
    {
        return string.Equals(origin, SeelenAppOrigin, StringComparison.Ordinal);
    }
}
