using System.Runtime.Versioning;
using Wallpaper.Core.FileOperations;
using Wallpaper.Infrastructure.Windows.Shell;

namespace Wallpaper.Infrastructure.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsShellContextMenuServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        $"wallpaper-shell-menu-{Guid.NewGuid():N}");

    public WindowsShellContextMenuServiceTests() => Directory.CreateDirectory(_rootPath);

    [Fact]
    public void ShowItemContextMenu_RejectsStaleTargetBeforeUsingOwnerWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new WindowsShellContextMenuService();
        var exception = Assert.Throws<FileCommandException>(() => service.ShowItemContextMenu(
            new FileCommandTarget(_rootPath, "missing.txt", FileCommandItemKind.File),
            ownerWindow: 0,
            screenX: 0,
            screenY: 0));

        Assert.Equal(FileCommandError.TargetMissing, exception.Error);
    }

    [Fact]
    public void ShowItemContextMenu_RejectsMissingOwnerWithoutChangingValidTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var filePath = Path.Combine(_rootPath, "keep.txt");
        File.WriteAllText(filePath, "keep");
        var service = new WindowsShellContextMenuService();

        var exception = Assert.Throws<ShellContextMenuException>(() => service.ShowItemContextMenu(
            new FileCommandTarget(_rootPath, "keep.txt", FileCommandItemKind.File),
            ownerWindow: 0,
            screenX: 0,
            screenY: 0));

        Assert.Equal("Windows Shell 메뉴의 소유 창을 찾을 수 없습니다.", exception.Message);
        Assert.Equal("keep", File.ReadAllText(filePath));
    }

    [Fact]
    public void ShowDesktopContextMenu_RejectsMissingOwnerWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new WindowsShellContextMenuService();

        var exception = Assert.Throws<ShellContextMenuException>(() =>
            service.ShowDesktopContextMenu(ownerWindow: 0, screenX: 0, screenY: 0));

        Assert.Equal("Windows Shell 메뉴의 소유 창을 찾을 수 없습니다.", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
