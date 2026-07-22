namespace Wallpaper.Hosts;

internal static class WallpaperEngineInputScope
{
    public static bool Contains(
        int pointX,
        int pointY,
        int left,
        int top,
        int right,
        int bottom) =>
        pointX >= left &&
        pointX < right &&
        pointY >= top &&
        pointY < bottom;
}
