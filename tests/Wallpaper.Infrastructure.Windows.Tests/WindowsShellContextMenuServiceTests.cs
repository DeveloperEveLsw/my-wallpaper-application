using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Wallpaper.Core.FileOperations;
using Wallpaper.Infrastructure.Windows.Shell;

namespace Wallpaper.Infrastructure.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsShellContextMenuServiceTests : IDisposable
{
    private const uint CmicMaskNoAsync = 0x00000100;
    private const uint CmicMaskUnicode = 0x00004000;
    private const uint CmicMaskPtInvoke = 0x20000000;

    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        $"wallpaper-shell-menu-{Guid.NewGuid():N}");

    public WindowsShellContextMenuServiceTests() => Directory.CreateDirectory(_rootPath);

    [Fact]
    public void CreateCommandInvokeMask_DefaultAllowsTheShellCommandToReturnAsynchronously()
    {
        var mask = WindowsShellContextMenuService.CreateCommandInvokeMask(
            ShellContextMenuShowOptions.None);

        Assert.Equal(CmicMaskUnicode | CmicMaskPtInvoke, mask);
        Assert.Equal(0U, mask & CmicMaskNoAsync);
    }

    [Fact]
    public void CreateCommandInvokeMask_SynchronousOptionKeepsTransientHostAliveForTheCommand()
    {
        var mask = WindowsShellContextMenuService.CreateCommandInvokeMask(
            ShellContextMenuShowOptions.RequestSynchronousCommand);

        Assert.Equal(CmicMaskUnicode | CmicMaskPtInvoke | CmicMaskNoAsync, mask);
    }

    [Fact]
    public void CreateCommandInvokeMask_RejectsUnknownOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowsShellContextMenuService.CreateCommandInvokeMask(
                (ShellContextMenuShowOptions)2));
    }

    [Fact]
    public void ShellCommandHostLifetime_PublishesReferencesAndWaitsForBorrowedReference()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(_ =>
        {
            var initializeResult = CoInitializeEx(0, 0x2);
            Assert.True(initializeResult >= 0);

            try
            {
                using var lifetime = ShellCommandHostLifetime.Create(
                    ShellContextMenuShowOptions.RequestSynchronousCommand);
                Assert.NotNull(lifetime);
                var ownerReferenceCount = lifetime.ReferenceCount;
                Assert.True(ownerReferenceCount > 0);

                Assert.True(SHGetThreadRef(out var threadReference) >= 0);
                Assert.NotEqual(0, threadReference);
                Assert.True(GetProcessReference(out var processReference) >= 0);
                Assert.Equal(threadReference, processReference);
                Assert.Equal(ownerReferenceCount + 2, lifetime.ReferenceCount);

                _ = Marshal.Release(processReference);
                var releaseTask = Task.Run(() =>
                {
                    Thread.Sleep(150);
                    _ = Marshal.Release(threadReference);
                });

                lifetime.CompleteAndWait();

                Assert.True(releaseTask.IsCompleted);
                Assert.Equal(0, lifetime.ReferenceCount);
            }
            finally
            {
                CoUninitialize();
            }
        });
    }

    [Fact]
    public void CreateItemContextMenu_RejectsStaleTargetBeforeUsingOwnerWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new WindowsShellContextMenuService();
        var exception = Assert.Throws<FileCommandException>(() => service.CreateItemContextMenu(
            new FileCommandTarget(_rootPath, "missing.txt", FileCommandItemKind.File),
            ownerWindow: 0));

        Assert.Equal(FileCommandError.TargetMissing, exception.Error);
    }

    [Fact]
    public void CreateItemContextMenu_RejectsMissingOwnerWithoutChangingValidTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var filePath = Path.Combine(_rootPath, "keep.txt");
        File.WriteAllText(filePath, "keep");
        var service = new WindowsShellContextMenuService();

        var exception = Assert.Throws<ShellContextMenuException>(() => service.CreateItemContextMenu(
            new FileCommandTarget(_rootPath, "keep.txt", FileCommandItemKind.File),
            ownerWindow: 0));

        Assert.Equal("Windows Shell 메뉴의 소유 창을 찾을 수 없습니다.", exception.Message);
        Assert.Equal("keep", File.ReadAllText(filePath));
    }

    [Fact]
    public void CreateDesktopContextMenu_RejectsMissingOwnerWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new WindowsShellContextMenuService();

        var exception = Assert.Throws<ShellContextMenuException>(() =>
            service.CreateDesktopContextMenu(ownerWindow: 0));

        Assert.Equal("Windows Shell 메뉴의 소유 창을 찾을 수 없습니다.", exception.Message);
    }

    [Fact]
    public void CreateItemContextMenu_CreatesPopulatedNativeMenuInStaSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var filePath = Path.Combine(_rootPath, "commands.txt");
        File.WriteAllText(filePath, "commands");

        RunOnStaThread(ownerWindow =>
        {
            var service = new WindowsShellContextMenuService();
            using var session = service.CreateItemContextMenu(
                new FileCommandTarget(_rootPath, "commands.txt", FileCommandItemKind.File),
                ownerWindow);

            Assert.True(session.NativeMenuItemCount > 0);
        });
    }

    [Fact]
    public void CreateDesktopContextMenu_CreatesPopulatedViewBackgroundMenuInStaSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(ownerWindow =>
        {
            var service = new WindowsShellContextMenuService();
            using var session = service.CreateDesktopContextMenu(ownerWindow);

            Assert.True(session.NativeMenuItemCount > 0);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private static void RunOnStaThread(Action<nint> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            nint ownerWindow = 0;
            try
            {
                ownerWindow = CreateWindowEx(
                    0,
                    "STATIC",
                    "Wallpaper Shell Menu Test Owner",
                    0,
                    0,
                    0,
                    1,
                    1,
                    0,
                    0,
                    0,
                    0);
                Assert.NotEqual(0, ownerWindow);
                action(ownerWindow);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (ownerWindow != 0)
                {
                    _ = DestroyWindow(ownerWindow);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parentWindow,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("shlwapi.dll", PreserveSig = true)]
    private static extern int SHGetThreadRef(out nint reference);

    [DllImport("api-ms-win-shcore-thread-l1-1-0.dll", PreserveSig = true)]
    private static extern int GetProcessReference(out nint reference);
}
