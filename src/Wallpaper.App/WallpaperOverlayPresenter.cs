using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Wallpaper.App;

internal sealed class WallpaperOverlayPresenter : IDisposable
{
    private const int WindowStyleIndex = -16;
    private const long ChildWindowStyle = 0x40000000L;
    private const long PopupWindowStyle = unchecked((long)0x80000000L);
    private const uint NoActivate = 0x0010;
    private const uint FrameChanged = 0x0020;
    private const uint ShowWindowFlag = 0x0040;

    private readonly DispatcherTimer _placementTimer;
    private readonly Window _window;
    private nint _parentWindowHandle;
    private bool _disposed;

    public WallpaperOverlayPresenter(Grid compositionRoot, UIElement backgroundLayer)
    {
        ArgumentNullException.ThrowIfNull(compositionRoot);
        ArgumentNullException.ThrowIfNull(backgroundLayer);

        Root = new Grid
        {
            Background = System.Windows.Media.Brushes.Transparent,
            DataContext = compositionRoot.DataContext,
            Focusable = true,
        };

        var overlayChildren = compositionRoot.Children
            .Cast<UIElement>()
            .Where(child => !ReferenceEquals(child, backgroundLayer))
            .ToArray();
        foreach (var child in overlayChildren)
        {
            compositionRoot.Children.Remove(child);
            Root.Children.Add(child);
        }

        _window = new Window
        {
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Content = Root,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(247, 249, 255)),
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None,
        };
        _window.SourceInitialized += Window_OnSourceInitialized;

        _placementTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _placementTimer.Tick += PlacementTimer_OnTick;
    }

    public Grid Root { get; }

    public void Show(nint parentWindowHandle, double width, double height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (parentWindowHandle == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        _parentWindowHandle = parentWindowHandle;
        Resize(width, height);
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        TryPlaceInsideParent();
        _placementTimer.Start();
    }

    public void Resize(double width, double height)
    {
        if (_disposed || width <= 0 || height <= 0)
        {
            return;
        }

        Root.Width = width;
        Root.Height = height;
        _window.Width = width;
        _window.Height = height;
    }

    public void Hide()
    {
        _placementTimer.Stop();
        if (_window.IsVisible)
        {
            _window.Hide();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _placementTimer.Stop();
        _placementTimer.Tick -= PlacementTimer_OnTick;
        _window.SourceInitialized -= Window_OnSourceInitialized;
        _window.Content = null;
        _window.Close();
    }

    private void Window_OnSourceInitialized(object? sender, EventArgs e) => TryPlaceInsideParent();

    private void PlacementTimer_OnTick(object? sender, EventArgs e) => TryPlaceInsideParent();

    private void TryPlaceInsideParent()
    {
        try
        {
            PlaceInsideParent();
        }
        catch (Win32Exception exception)
        {
            Trace.TraceWarning($"Wallpaper overlay placement failed: {exception.Message}");
        }
    }

    private void PlaceInsideParent()
    {
        if (_disposed || !_window.IsVisible || _parentWindowHandle == 0)
        {
            return;
        }

        var windowHandle = new WindowInteropHelper(_window).Handle;
        if (windowHandle == 0)
        {
            return;
        }

        if (!GetClientRect(_parentWindowHandle, out var parentRectangle))
        {
            return;
        }

        var width = parentRectangle.Right - parentRectangle.Left;
        var height = parentRectangle.Bottom - parentRectangle.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (!Root.Width.Equals((double)width) || !Root.Height.Equals((double)height))
        {
            Root.Width = width;
            Root.Height = height;
            _window.Width = width;
            _window.Height = height;
            Root.UpdateLayout();
        }

        var style = GetWindowLongPtr(windowHandle, WindowStyleIndex).ToInt64();
        var overlayStyle = (style | ChildWindowStyle) & ~PopupWindowStyle;
        if (overlayStyle != style)
        {
            _ = SetWindowLongPtr(windowHandle, WindowStyleIndex, new nint(overlayStyle));
        }

        if (GetParent(windowHandle) != _parentWindowHandle)
        {
            _ = SetParent(windowHandle, _parentWindowHandle);
            if (GetParent(windowHandle) != _parentWindowHandle)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        if (!SetWindowPos(
                windowHandle,
                0,
                0,
                0,
                width,
                height,
                NoActivate | FrameChanged | ShowWindowFlag))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    private static extern nint GetParent(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint childWindowHandle, nint newParentWindowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

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
