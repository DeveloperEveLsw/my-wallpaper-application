namespace Wallpaper.Hosts;

public sealed record HostLaunchOptions(
    bool UseDevelopmentWindow,
    nint ParentWindowHandle)
{
    public static HostLaunchOptions Resolve(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var useDevelopmentWindow = false;
        nint parentWindowHandle = 0;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (argument.Equals("--dev-window", StringComparison.OrdinalIgnoreCase))
            {
                useDevelopmentWindow = true;
                continue;
            }

            if (argument.Equals("--host", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--host=", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "--host 옵션은 제거되었습니다. Wallpaper Engine의 -parentHWND 인자를 사용하거나 "
                    + "로컬 디버깅 시 --dev-window를 사용하세요.");
            }

            string? parentHandleValue = null;
            if (argument.Equals("-parentHWND", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("--parent-hwnd", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= arguments.Count)
                {
                    throw new ArgumentException($"{argument} 뒤에 HWND 값이 필요합니다.");
                }

                parentHandleValue = arguments[++index];
            }
            else if (argument.StartsWith("--parent-hwnd=", StringComparison.OrdinalIgnoreCase))
            {
                parentHandleValue = argument["--parent-hwnd=".Length..];
            }

            if (parentHandleValue is null)
            {
                continue;
            }

            if (!long.TryParse(parentHandleValue, out var parsed) || parsed <= 0)
            {
                throw new ArgumentException($"유효하지 않은 parent HWND 값입니다: {parentHandleValue}");
            }

            parentWindowHandle = checked((nint)parsed);
        }

        if (useDevelopmentWindow && parentWindowHandle != 0)
        {
            throw new ArgumentException(
                "--dev-window와 Wallpaper Engine parent HWND 인자를 함께 사용할 수 없습니다.");
        }

        if (useDevelopmentWindow)
        {
            return new HostLaunchOptions(true, 0);
        }

        if (parentWindowHandle == 0)
        {
            throw new ArgumentException(
                "Wallpaper Engine이 -parentHWND 인자로 실행해야 합니다. "
                + "로컬 디버깅 시에만 --dev-window를 사용하세요.");
        }

        return new HostLaunchOptions(false, parentWindowHandle);
    }
}
