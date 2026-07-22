using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Wallpaper.Hosts;

[SupportedOSPlatform("windows")]
internal sealed class WindowsWallpaperEngineInterop : IWallpaperEngineInterop
{
    private const string WallpaperEngineIntermediateWindowClass = "WPEAppIntermediateWorker";
    private const string DesktopProgramManagerWindowClass = "Progman";
    private const string DesktopViewWindowClass = "SHELLDLL_DefView";
    private const string DesktopWorkerWindowClass = "WorkerW";
    private const int WindowStyleIndex = -16;
    private const int ExtendedWindowStyleIndex = -20;
    private const long ChildWindowStyle = 0x40000000L;
    private const long PopupWindowStyle = unchecked((long)0x80000000L);
    private const long CaptionWindowStyle = 0x00C00000L;
    private const long ThickFrameWindowStyle = 0x00040000L;
    private const long SystemMenuWindowStyle = 0x00080000L;
    private const long MinimizeBoxWindowStyle = 0x00020000L;
    private const long MaximizeBoxWindowStyle = 0x00010000L;
    private const long TransparentExtendedWindowStyle = 0x00000020L;
    private const uint NoActivate = 0x0010;
    private const uint FrameChanged = 0x0020;
    private const uint ShowWindowFlag = 0x0040;
    private const uint NoMove = 0x0002;
    private const uint NoSize = 0x0001;
    private const uint NoOwnerZOrder = 0x0200;
    private const uint DwmWindowAttributeCloaked = 14;

    private readonly object _interactiveInputLock = new();
    private nint _interactiveIntermediateWindow;
    private nint _interactiveWorkerWindow;
    private nint _desktopViewWindow;
    private NativeRect _interactiveRegion;
    private bool _interactiveWorkerRaised;

    public bool IsWindow(nint windowHandle) => NativeIsWindow(windowHandle);

    public nint GetParentWindow(nint windowHandle) => GetParent(windowHandle);

    public string? GetWindowProcessName(nint windowHandle)
    {
        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    public bool IsWallpaperEngineRunning() =>
        HasRunningProcess("wallpaper32") || HasRunningProcess("wallpaper64");

    public bool IsParentConnectedToDesktop(nint parentWindowHandle)
    {
        if (!NativeIsWindow(parentWindowHandle))
        {
            return false;
        }

        var className = new StringBuilder(64);
        var classNameLength = GetClassName(parentWindowHandle, className, className.Capacity);
        if (classNameLength <= 0 ||
            !className.ToString().Equals(
                WallpaperEngineIntermediateWindowClass,
                StringComparison.Ordinal))
        {
            return true;
        }

        var desktopParent = GetParent(parentWindowHandle);
        return desktopParent != 0 && NativeIsWindow(desktopParent);
    }

    public bool IsRenderingVisible(nint windowHandle, nint parentWindowHandle)
    {
        if (!NativeIsWindowVisible(windowHandle) ||
            !NativeIsWindowVisible(parentWindowHandle) ||
            IsIconic(parentWindowHandle))
        {
            return false;
        }

        var cloaked = 0;
        var result = DwmGetWindowAttribute(
            windowHandle,
            DwmWindowAttributeCloaked,
            out cloaked,
            sizeof(int));
        return result < 0 || cloaked == 0;
    }

    public void PlaceInsideParent(nint windowHandle, nint parentWindowHandle)
    {
        if (!GetClientRect(parentWindowHandle, out var parentClientRect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var width = parentClientRect.Right - parentClientRect.Left;
        var height = parentClientRect.Bottom - parentClientRect.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var style = GetWindowLongPtr(windowHandle, WindowStyleIndex).ToInt64();
        var wallpaperStyle = (style | ChildWindowStyle) &
            ~(PopupWindowStyle |
              CaptionWindowStyle |
              ThickFrameWindowStyle |
              SystemMenuWindowStyle |
              MinimizeBoxWindowStyle |
              MaximizeBoxWindowStyle);
        var styleChanged = wallpaperStyle != style;
        if (styleChanged)
        {
            _ = SetWindowLongPtr(windowHandle, WindowStyleIndex, new nint(wallpaperStyle));
        }

        var parentChanged = false;
        if (GetParent(windowHandle) != parentWindowHandle)
        {
            _ = SetParent(windowHandle, parentWindowHandle);
            if (GetParent(windowHandle) != parentWindowHandle)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            parentChanged = true;
        }

        if (!GetWindowRect(windowHandle, out var windowRectangle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var windowTopLeft = new NativePoint(windowRectangle.Left, windowRectangle.Top);
        var windowBottomRight = new NativePoint(windowRectangle.Right, windowRectangle.Bottom);
        if (!ScreenToClient(parentWindowHandle, ref windowTopLeft) ||
            !ScreenToClient(parentWindowHandle, ref windowBottomRight))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var needsPlacement = styleChanged ||
            parentChanged ||
            !NativeIsWindowVisible(windowHandle) ||
            windowTopLeft.X != 0 ||
            windowTopLeft.Y != 0 ||
            windowBottomRight.X != width ||
            windowBottomRight.Y != height;
        if (!needsPlacement)
        {
            return;
        }

        var placementFlags = NoActivate | ShowWindowFlag;
        if (styleChanged)
        {
            placementFlags |= FrameChanged;
        }

        if (!SetWindowPos(
                windowHandle,
                0,
                0,
                0,
                width,
                height,
                placementFlags))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void EnsureInteractiveInput(nint parentWindowHandle)
    {
        lock (_interactiveInputLock)
        {
            if (_interactiveIntermediateWindow != parentWindowHandle ||
                !NativeIsWindow(_interactiveWorkerWindow) ||
                !NativeIsWindow(_desktopViewWindow))
            {
                RestoreInteractiveInputCore();
                CaptureInteractiveDesktop(parentWindowHandle);
            }

            EnableInteractiveWorker();
        }
    }

    public void RestoreInteractiveInput()
    {
        lock (_interactiveInputLock)
        {
            RestoreInteractiveInputCore();
        }
    }

    internal static nint GetInteractiveWorkerWindow(nint intermediateWindow)
    {
        if (!HasWindowClass(intermediateWindow, WallpaperEngineIntermediateWindowClass))
        {
            throw new InvalidOperationException(
                "Wallpaper Engine intermediate HWND를 확인하지 못했습니다.");
        }

        var workerWindow = GetParent(intermediateWindow);
        if (!HasWindowClass(workerWindow, DesktopWorkerWindowClass))
        {
            throw new InvalidOperationException(
                "Wallpaper Engine WorkerW HWND를 확인하지 못했습니다.");
        }

        var programManager = GetParent(workerWindow);
        if (!HasWindowClass(programManager, DesktopProgramManagerWindowClass))
        {
            throw new InvalidOperationException(
                "Explorer Desktop HWND 계층을 확인하지 못했습니다.");
        }

        return workerWindow;
    }

    internal static void RestoreInteractiveInputAfterProcessExit(
        nint intermediateWindow,
        nint workerWindow)
    {
        if ((NativeIsWindow(intermediateWindow) &&
             (!HasWindowClass(intermediateWindow, WallpaperEngineIntermediateWindowClass) ||
              GetParent(intermediateWindow) != workerWindow)) ||
            !HasWindowClass(workerWindow, DesktopWorkerWindowClass))
        {
            return;
        }

        var programManager = GetParent(workerWindow);
        if (!HasWindowClass(programManager, DesktopProgramManagerWindowClass) ||
            HasActiveWallpaperChild(workerWindow))
        {
            return;
        }

        var desktopView = FindWindowEx(programManager, 0, DesktopViewWindowClass, null);
        RestoreInteractiveWorker(workerWindow, desktopView);
    }

    private static bool HasActiveWallpaperChild(nint workerWindow)
    {
        var intermediateWindow = FindWindowEx(
            workerWindow,
            0,
            WallpaperEngineIntermediateWindowClass,
            null);
        while (intermediateWindow != 0)
        {
            if (FindWindowEx(intermediateWindow, 0, null, null) != 0)
            {
                return true;
            }

            intermediateWindow = FindWindowEx(
                workerWindow,
                intermediateWindow,
                WallpaperEngineIntermediateWindowClass,
                null);
        }

        return false;
    }

    private void CaptureInteractiveDesktop(nint intermediateWindow)
    {
        if (!HasWindowClass(intermediateWindow, WallpaperEngineIntermediateWindowClass))
        {
            throw new InvalidOperationException(
                "Wallpaper Engine intermediate HWND를 확인하지 못했습니다.");
        }

        var workerWindow = GetParent(intermediateWindow);
        if (!HasWindowClass(workerWindow, DesktopWorkerWindowClass))
        {
            throw new InvalidOperationException(
                "Wallpaper Engine WorkerW HWND를 확인하지 못했습니다.");
        }

        var programManager = GetParent(workerWindow);
        var desktopView = FindWindowEx(programManager, 0, DesktopViewWindowClass, null);
        if (!HasWindowClass(programManager, DesktopProgramManagerWindowClass) ||
            !NativeIsWindow(desktopView))
        {
            throw new InvalidOperationException(
                "Explorer Desktop HWND 계층을 확인하지 못했습니다.");
        }

        _interactiveIntermediateWindow = intermediateWindow;
        _interactiveWorkerWindow = workerWindow;
        _desktopViewWindow = desktopView;
        _interactiveRegion = default;
        _interactiveWorkerRaised = false;
    }

    private void EnableInteractiveWorker()
    {
        var workerWindow = _interactiveWorkerWindow;
        if (!NativeIsWindow(workerWindow))
        {
            throw new InvalidOperationException("상호작용할 Desktop HWND가 더 이상 유효하지 않습니다.");
        }

        var stateChanged = !IsWindowEnabled(workerWindow);
        if (stateChanged)
        {
            _ = EnableWindow(workerWindow, true);
        }

        if (!IsWindowEnabled(workerWindow))
        {
            throw new InvalidOperationException("Wallpaper Engine WorkerW를 활성화하지 못했습니다.");
        }

        var extendedStyle = GetWindowLongPtr(workerWindow, ExtendedWindowStyleIndex).ToInt64();
        var interactiveStyle = extendedStyle & ~TransparentExtendedWindowStyle;
        var styleChanged = interactiveStyle != extendedStyle;
        if (styleChanged)
        {
            _ = SetWindowLongPtr(
                workerWindow,
                ExtendedWindowStyleIndex,
                new nint(interactiveStyle));
        }

        var regionChanged = EnsureInteractiveRegion();
        if (!stateChanged && !styleChanged && !regionChanged && _interactiveWorkerRaised)
        {
            return;
        }

        if (!SetWindowPos(
                workerWindow,
                0,
                0,
                0,
                0,
                0,
                NoMove | NoSize | NoActivate | NoOwnerZOrder | FrameChanged | ShowWindowFlag))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _interactiveWorkerRaised = true;
    }

    private void RestoreInteractiveInputCore()
    {
        var workerWindow = _interactiveWorkerWindow;
        var desktopView = _desktopViewWindow;
        _interactiveIntermediateWindow = 0;
        _interactiveWorkerWindow = 0;
        _desktopViewWindow = 0;
        _interactiveRegion = default;
        _interactiveWorkerRaised = false;

        if (!NativeIsWindow(workerWindow))
        {
            return;
        }

        RestoreInteractiveWorker(workerWindow, desktopView);
    }

    private static void RestoreInteractiveWorker(nint workerWindow, nint desktopView)
    {
        if (!NativeIsWindow(workerWindow))
        {
            return;
        }

        var extendedStyle = GetWindowLongPtr(workerWindow, ExtendedWindowStyleIndex).ToInt64();
        _ = SetWindowLongPtr(
            workerWindow,
            ExtendedWindowStyleIndex,
            new nint(extendedStyle | TransparentExtendedWindowStyle));
        _ = EnableWindow(workerWindow, false);
        _ = SetWindowRgn(workerWindow, 0, true);

        if (NativeIsWindow(desktopView))
        {
            _ = SetWindowPos(
                desktopView,
                0,
                0,
                0,
                0,
                0,
                NoMove | NoSize | NoActivate | NoOwnerZOrder | FrameChanged);
        }
    }

    private bool EnsureInteractiveRegion()
    {
        if (!GetWindowRect(_interactiveIntermediateWindow, out var intermediateRectangle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var topLeft = new NativePoint(intermediateRectangle.Left, intermediateRectangle.Top);
        var bottomRight = new NativePoint(intermediateRectangle.Right, intermediateRectangle.Bottom);
        if (!ScreenToClient(_interactiveWorkerWindow, ref topLeft) ||
            !ScreenToClient(_interactiveWorkerWindow, ref bottomRight))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var desiredRegion = new NativeRect
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = bottomRight.X,
            Bottom = bottomRight.Y,
        };
        if (desiredRegion.Equals(_interactiveRegion) &&
            HasExpectedWindowRegion(_interactiveWorkerWindow, desiredRegion))
        {
            return false;
        }

        var region = CreateRectRgn(
            desiredRegion.Left,
            desiredRegion.Top,
            desiredRegion.Right,
            desiredRegion.Bottom);
        if (region == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (SetWindowRgn(_interactiveWorkerWindow, region, true) == 0)
        {
            _ = DeleteObject(region);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _interactiveRegion = desiredRegion;
        return true;
    }

    private static bool HasExpectedWindowRegion(nint windowHandle, NativeRect expectedRegion)
    {
        var actual = CreateRectRgn(0, 0, 0, 0);
        var expected = CreateRectRgn(
            expectedRegion.Left,
            expectedRegion.Top,
            expectedRegion.Right,
            expectedRegion.Bottom);
        if (actual == 0 || expected == 0)
        {
            if (actual != 0)
            {
                _ = DeleteObject(actual);
            }

            if (expected != 0)
            {
                _ = DeleteObject(expected);
            }

            return false;
        }

        try
        {
            return GetWindowRgn(windowHandle, actual) != 0 && EqualRgn(actual, expected);
        }
        finally
        {
            _ = DeleteObject(actual);
            _ = DeleteObject(expected);
        }
    }

    private static bool HasWindowClass(nint windowHandle, string expectedClass)
    {
        if (!NativeIsWindow(windowHandle))
        {
            return false;
        }

        var className = new StringBuilder(64);
        return GetClassName(windowHandle, className, className.Capacity) > 0 &&
            className.ToString().Equals(expectedClass, StringComparison.Ordinal);
    }

    private static bool HasRunningProcess(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeIsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint windowHandle,
        StringBuilder className,
        int maxCount);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(
        nint parentWindowHandle,
        nint childAfterWindowHandle,
        string? className,
        string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint childWindowHandle, nint newParentWindowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(nint windowHandle, bool enable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(
        nint windowHandle,
        ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeIsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EqualRgn(nint firstRegion, nint secondRegion);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(
        nint windowHandle,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowRgn(nint windowHandle, nint region);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint windowHandle,
        uint attribute,
        out int value,
        int valueSize);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }
}
