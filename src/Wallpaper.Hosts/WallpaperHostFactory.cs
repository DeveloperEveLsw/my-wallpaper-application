using Wallpaper.Rendering.Abstractions;

namespace Wallpaper.Hosts;

public static class WallpaperHostFactory
{
    public static IWallpaperHost Create(
        IWallpaperRenderLifecycle renderLifecycle,
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(renderLifecycle);
        ArgumentNullException.ThrowIfNull(arguments);

        var options = HostLaunchOptions.Resolve(
            arguments,
            Environment.GetEnvironmentVariable(HostLaunchOptions.EnvironmentVariableName),
            WindowsProcessTree.TryGetParentProcessName(),
            AppContext.BaseDirectory);

        return options.Kind switch
        {
            HostKind.Standalone => new StandaloneWallpaperHost(),
            HostKind.WallpaperEngine => new WallpaperEngineHost(
                renderLifecycle,
                options.ParentWindowHandle),
            _ => throw new InvalidOperationException($"지원하지 않는 호스트 형식입니다: {options.Kind}"),
        };
    }
}
