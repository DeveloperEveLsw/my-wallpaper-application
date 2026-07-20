using Wallpaper.Core.FileOperations;
using System.Runtime.Versioning;

namespace Wallpaper.Infrastructure.Windows.Shell;

[SupportedOSPlatform("windows")]
public interface IShellContextMenuService
{
    void ShowItemContextMenu(
        FileCommandTarget target,
        nint ownerWindow,
        int screenX,
        int screenY);

    void ShowDesktopContextMenu(
        nint ownerWindow,
        int screenX,
        int screenY);
}
