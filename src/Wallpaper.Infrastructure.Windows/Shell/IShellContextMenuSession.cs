using System.Runtime.Versioning;

namespace Wallpaper.Infrastructure.Windows.Shell;

[SupportedOSPlatform("windows")]
public interface IShellContextMenuSession : IDisposable
{
    IReadOnlyList<ShellContextMenuEntry> Entries { get; }

    void Invoke(uint commandId, int screenX, int screenY);

    void ShowClassic(int screenX, int screenY);
}
