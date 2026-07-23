using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Wallpaper.App;

internal sealed class WallpaperEnginePresentation : IWallpaperPresentation
{
    private const int ChildWindowStyle = 0x40000000;
    private const int VisibleWindowStyle = 0x10000000;
    private const int WindowDpiChangedMessage = 0x02E0;

    private readonly HwndSource _source;
    private bool _disposed;

    public WallpaperEnginePresentation(
        nint parentWindowHandle,
        Func<nint, WallpaperView> createView)
    {
        ArgumentNullException.ThrowIfNull(createView);
        if (parentWindowHandle == 0 || !IsWindow(parentWindowHandle))
        {
            throw new ArgumentException(
                "Wallpaper Engine의 유효한 parent HWND가 필요합니다.",
                nameof(parentWindowHandle));
        }

        if (!GetClientRect(parentWindowHandle, out var parentRectangle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var width = parentRectangle.Right - parentRectangle.Left;
        var height = parentRectangle.Bottom - parentRectangle.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                "Wallpaper Engine parent HWND의 표시 영역이 비어 있습니다.");
        }

        var parameters = new HwndSourceParameters("Wallpaper.Application")
        {
            ParentWindow = parentWindowHandle,
            PositionX = 0,
            PositionY = 0,
            Width = width,
            Height = height,
            WindowStyle = ChildWindowStyle | VisibleWindowStyle,
            TreatAsInputRoot = true,
            AcquireHwndFocusInMenuMode = true,
        };

        _source = new HwndSource(parameters);
        try
        {
            Handle = _source.Handle;
            View = createView(Handle);
            _source.RootVisual = View;
            _source.AddHook(SourceWindowHook);
        }
        catch
        {
            _source.Dispose();
            throw;
        }
    }

    public nint Handle { get; }

    public WallpaperView View { get; }

    public event EventHandler? Closed
    {
        add { }
        remove { }
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(SourceWindowHook);
        _source.RootVisual = null;
        _source.Dispose();
    }

    private nint SourceWindowHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == WindowDpiChangedMessage)
        {
            _ = View.ReloadFileVisualsForDpiAsync();
        }

        return 0;
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
