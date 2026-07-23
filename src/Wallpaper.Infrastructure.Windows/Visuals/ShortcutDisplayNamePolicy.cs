using System.IO;

namespace Wallpaper.Infrastructure.Windows.Visuals;

public static class ShortcutDisplayNamePolicy
{
    public static bool IsShortcutExtension(string extension) =>
        string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase);

    public static string CreateFallback(string fileName, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!IsShortcutExtension(extension))
        {
            return fileName;
        }

        var displayName = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(displayName) ? fileName : displayName;
    }

    public static string NormalizeShellDisplayName(
        string? shellDisplayName,
        string fallbackFileName,
        string extension)
    {
        var displayName = string.IsNullOrWhiteSpace(shellDisplayName)
            ? CreateFallback(fallbackFileName, extension)
            : shellDisplayName.Trim();
        if (!IsShortcutExtension(extension) ||
            !displayName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return displayName;
        }

        var withoutShortcutExtension = displayName[..^extension.Length];
        return string.IsNullOrWhiteSpace(withoutShortcutExtension)
            ? displayName
            : withoutShortcutExtension;
    }
}
