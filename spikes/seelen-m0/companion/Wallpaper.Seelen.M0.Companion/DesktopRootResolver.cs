namespace Wallpaper.Seelen.M0.Companion;

internal static class DesktopRootResolver
{
    public static string Resolve()
    {
        var desktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrWhiteSpace(desktop))
        {
            throw new InvalidOperationException("Windows Desktop folder could not be resolved.");
        }

        var fullPath = Path.GetFullPath(desktop);
        if (!Path.IsPathRooted(fullPath))
        {
            throw new InvalidOperationException("Windows Desktop folder is not an absolute path.");
        }

        return fullPath;
    }
}
