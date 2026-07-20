namespace Wallpaper.Core.Models;

public static class DesktopItemId
{
    public static string ForFile(string relativePath) => Create("file", relativePath);

    public static string ForFolder(string relativePath) => Create("folder", relativePath);

    private static string Create(string kind, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return $"{kind}:{relativePath.Replace('\\', '/').ToUpperInvariant()}";
    }
}
