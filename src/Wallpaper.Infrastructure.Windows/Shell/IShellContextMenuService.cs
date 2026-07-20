using Wallpaper.Core.FileOperations;
using System.Runtime.Versioning;

namespace Wallpaper.Infrastructure.Windows.Shell;

[SupportedOSPlatform("windows")]
public interface IShellContextMenuService
{
    IShellContextMenuSession CreateItemContextMenu(
        FileCommandTarget target,
        nint ownerWindow);

    IShellContextMenuSession CreateDesktopContextMenu(nint ownerWindow);
}
