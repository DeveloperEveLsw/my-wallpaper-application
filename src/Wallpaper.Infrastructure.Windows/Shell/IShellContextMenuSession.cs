using System.Runtime.Versioning;

namespace Wallpaper.Infrastructure.Windows.Shell;

[SupportedOSPlatform("windows")]
public interface IShellContextMenuSession : IDisposable
{
    int NativeMenuItemCount { get; }

    bool Show(
        int screenX,
        int screenY,
        ShellContextMenuShowOptions options = ShellContextMenuShowOptions.None);
}
