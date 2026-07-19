using Wallpaper.Core.Models;

namespace Wallpaper.Core.Scanning;

public interface IDesktopScanner
{
    DesktopSnapshot Scan(string rootPath);
}
