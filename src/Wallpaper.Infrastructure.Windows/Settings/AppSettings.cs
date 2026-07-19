namespace Wallpaper.Infrastructure.Windows.Settings;

public sealed record AppSettings(
    int SchemaVersion,
    string? RootPath,
    IReadOnlyList<string> FolderOrder)
{
    public const int CurrentSchemaVersion = 1;

    public static AppSettings Default { get; } = new(
        CurrentSchemaVersion,
        null,
        Array.Empty<string>());
}
