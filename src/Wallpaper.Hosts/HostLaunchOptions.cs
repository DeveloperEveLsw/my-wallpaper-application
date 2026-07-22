namespace Wallpaper.Hosts;

public sealed record HostLaunchOptions(
    HostKind Kind,
    bool WasExplicit,
    nint ParentWindowHandle = 0)
{
    private const string HostEnvironmentVariable = "WALLPAPER_HOST";

    public static HostLaunchOptions Resolve(
        IReadOnlyList<string> arguments,
        string? environmentHost,
        string? parentProcessName,
        string? applicationBaseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var parentWindowHandle = ReadParentWindowHandle(arguments);
        var argumentHost = ReadHostArgument(arguments);
        if (argumentHost is not null)
        {
            return Create(ParseHost(argumentHost), wasExplicit: true, parentWindowHandle);
        }

        if (!string.IsNullOrWhiteSpace(environmentHost))
        {
            return Create(ParseHost(environmentHost), wasExplicit: true, parentWindowHandle);
        }

        if (parentWindowHandle != 0)
        {
            return new HostLaunchOptions(
                HostKind.WallpaperEngine,
                WasExplicit: false,
                parentWindowHandle);
        }

        var normalizedParent = Path.GetFileNameWithoutExtension(parentProcessName ?? string.Empty);
        if (normalizedParent.Equals("wallpaper32", StringComparison.OrdinalIgnoreCase) ||
            normalizedParent.Equals("wallpaper64", StringComparison.OrdinalIgnoreCase))
        {
            return new HostLaunchOptions(HostKind.WallpaperEngine, WasExplicit: false);
        }

        var normalizedBaseDirectory = (applicationBaseDirectory ?? string.Empty)
            .Replace('\\', '/')
            .TrimEnd('/');
        if (normalizedBaseDirectory.Contains(
                "/wallpaper_engine/projects/",
                StringComparison.OrdinalIgnoreCase))
        {
            return new HostLaunchOptions(HostKind.WallpaperEngine, WasExplicit: false);
        }

        return new HostLaunchOptions(HostKind.Standalone, WasExplicit: false);
    }

    public static string EnvironmentVariableName => HostEnvironmentVariable;

    private static HostLaunchOptions Create(
        HostKind kind,
        bool wasExplicit,
        nint parentWindowHandle) =>
        new(
            kind,
            wasExplicit,
            kind == HostKind.WallpaperEngine ? parentWindowHandle : 0);

    private static string? ReadHostArgument(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument.StartsWith("--host=", StringComparison.OrdinalIgnoreCase))
            {
                return argument["--host=".Length..];
            }

            if (argument.Equals("--host", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= arguments.Count)
                {
                    throw new ArgumentException("--host 뒤에 호스트 이름이 필요합니다.");
                }

                return arguments[index + 1];
            }
        }

        return null;
    }

    private static nint ReadParentWindowHandle(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            string? value = null;
            if (argument.Equals("-parentHWND", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--parent-hwnd", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= arguments.Count)
                {
                    throw new ArgumentException($"{argument} 뒤에 HWND 값이 필요합니다.");
                }

                value = arguments[index + 1];
            }
            else if (argument.StartsWith("--parent-hwnd=", StringComparison.OrdinalIgnoreCase))
            {
                value = argument["--parent-hwnd=".Length..];
            }

            if (value is null)
            {
                continue;
            }

            if (!long.TryParse(value, out var parsed) || parsed <= 0)
            {
                throw new ArgumentException($"유효하지 않은 parent HWND 값입니다: {value}");
            }

            return checked((nint)parsed);
        }

        return 0;
    }

    private static HostKind ParseHost(string value)
    {
        var normalized = value.Trim().Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "standalone" => HostKind.Standalone,
            "wallpaper-engine" or "wallpaperengine" or "we" => HostKind.WallpaperEngine,
            _ => throw new ArgumentException(
                $"지원하지 않는 호스트 '{value}'입니다. standalone 또는 wallpaper-engine을 사용하세요."),
        };
    }
}
