namespace Wallpaper.Core.Models;

public sealed record ScanWarning(
    string RelativePath,
    ScanWarningCode Code);

public enum ScanWarningCode
{
    Inaccessible,
    DisappearedDuringScan,
    UnsupportedEntry,
}
