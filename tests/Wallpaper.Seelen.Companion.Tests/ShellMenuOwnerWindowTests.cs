using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Wallpaper.Seelen.Companion;

namespace Wallpaper.Seelen.Companion.Tests;

[SupportedOSPlatform("windows")]
public sealed class ShellMenuOwnerWindowTests
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;

    [Fact]
    public void Create_ProducesTransparentLayeredToolWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnStaThread(() =>
        {
            using var owner = ShellMenuBroker.ShellMenuOwnerWindow.Create(100, 100);

            Assert.NotEqual(0, owner.Handle);
            Assert.True(IsWindow(owner.Handle));
            var extendedStyle = GetWindowLong(owner.Handle, GwlExStyle);
            Assert.Equal(WsExLayered, extendedStyle & WsExLayered);
            Assert.Equal(WsExToolWindow, extendedStyle & WsExToolWindow);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
}
