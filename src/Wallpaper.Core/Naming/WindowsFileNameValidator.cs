namespace Wallpaper.Core.Naming;

public static class WindowsFileNameValidator
{
    private static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static FileNameValidationResult Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return FileNameValidationResult.Invalid(FileNameError.Empty);
        }

        if (name is "." or "..")
        {
            return FileNameValidationResult.Invalid(FileNameError.RelativeSegment);
        }

        if (name[^1] is ' ' or '.')
        {
            return FileNameValidationResult.Invalid(FileNameError.TrailingSpaceOrPeriod);
        }

        if (name.Any(character => character < ' ' || InvalidCharacters.Contains(character)))
        {
            return FileNameValidationResult.Invalid(FileNameError.InvalidCharacter);
        }

        var deviceName = Path.GetFileNameWithoutExtension(name);
        if (ReservedNames.Contains(deviceName))
        {
            return FileNameValidationResult.Invalid(FileNameError.ReservedDeviceName);
        }

        return FileNameValidationResult.Valid;
    }
}

public readonly record struct FileNameValidationResult(bool IsValid, FileNameError? Error)
{
    public static FileNameValidationResult Valid { get; } = new(true, null);

    public static FileNameValidationResult Invalid(FileNameError error) => new(false, error);
}

public enum FileNameError
{
    Empty,
    RelativeSegment,
    TrailingSpaceOrPeriod,
    InvalidCharacter,
    ReservedDeviceName,
}
