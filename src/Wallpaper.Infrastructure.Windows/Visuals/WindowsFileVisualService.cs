#if WINDOWS
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wallpaper.Core.Models;

namespace Wallpaper.Infrastructure.Windows.Visuals;

public sealed class WindowsFileVisualService : IFileVisualService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const int DefaultCacheCapacity = 512;

    private static readonly HashSet<string> ImageExtensions = new(
        [".bmp", ".dib", ".gif", ".heic", ".heif", ".jfif", ".jpe", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"],
        StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<VisualCacheKey, Lazy<Task<ImageSource?>>> _cache = new();
    private readonly ConcurrentQueue<VisualCacheKey> _insertionOrder = new();
    private readonly int _cacheCapacity;

    public WindowsFileVisualService(int cacheCapacity = DefaultCacheCapacity)
    {
        if (cacheCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheCapacity));
        }

        _cacheCapacity = cacheCapacity;
    }

    public Task<ImageSource?> LoadShellIconAsync(
        DesktopFile file,
        string absolutePath,
        int targetPixelWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetPixelWidth);

        var key = CreateKey(VisualKind.ShellIcon, file, absolutePath, targetPixelWidth);
        return GetOrLoadAsync(
            key,
            () => LoadShellIcon(absolutePath, targetPixelWidth),
            cancellationToken);
    }

    public Task<ImageSource?> LoadThumbnailAsync(
        DesktopFile file,
        string absolutePath,
        int targetPixelWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetPixelWidth);

        if (!ImageExtensions.Contains(file.Extension))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        var key = CreateKey(VisualKind.Thumbnail, file, absolutePath, targetPixelWidth);
        return GetOrLoadAsync(
            key,
            () => LoadThumbnail(absolutePath, targetPixelWidth),
            cancellationToken);
    }

    private async Task<ImageSource?> GetOrLoadAsync(
        VisualCacheKey key,
        Func<ImageSource?> loader,
        CancellationToken cancellationToken)
    {
        var candidate = new Lazy<Task<ImageSource?>>(
            () => Task.Run(loader),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var cached = _cache.GetOrAdd(key, candidate);
        if (ReferenceEquals(candidate, cached))
        {
            _insertionOrder.Enqueue(key);
            TrimCache();
        }

        return await cached.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void TrimCache()
    {
        while (_cache.Count > _cacheCapacity && _insertionOrder.TryDequeue(out var key))
        {
            _cache.TryRemove(key, out _);
        }
    }

    private static ImageSource? LoadShellIcon(string absolutePath, int targetPixelWidth)
    {
        IShellItemImageFactory? imageFactory = null;
        nint bitmapHandle = 0;
        try
        {
            var interfaceId = typeof(IShellItemImageFactory).GUID;
            var createResult = SHCreateItemFromParsingName(
                absolutePath,
                bindContext: 0,
                ref interfaceId,
                out imageFactory);
            if (createResult < 0 || imageFactory is null)
            {
                return LoadLegacyShellIcon(absolutePath);
            }

            var imageResult = imageFactory.GetImage(
                new NativeSize(targetPixelWidth, targetPixelWidth),
                ShellItemImageFactoryFlags.BiggerSizeOk | ShellItemImageFactoryFlags.IconOnly,
                out bitmapHandle);
            if (imageResult < 0 || bitmapHandle == 0)
            {
                return LoadLegacyShellIcon(absolutePath);
            }

            var image = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                palette: 0,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                ExternalException)
        {
            return LoadLegacyShellIcon(absolutePath);
        }
        finally
        {
            if (bitmapHandle != 0)
            {
                _ = DeleteObject(bitmapHandle);
            }

            if (imageFactory is not null && Marshal.IsComObject(imageFactory))
            {
                _ = Marshal.FinalReleaseComObject(imageFactory);
            }
        }
    }

    private static ImageSource? LoadLegacyShellIcon(string absolutePath)
    {
        try
        {
            var fileInfo = new ShellFileInfo();
            var result = SHGetFileInfo(
                absolutePath,
                fileAttributes: 0,
                ref fileInfo,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiIcon | ShgfiLargeIcon);
            if (result == 0 || fileInfo.IconHandle == 0)
            {
                return null;
            }

            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(
                    fileInfo.IconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally
            {
                _ = DestroyIcon(fileInfo.IconHandle);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                ExternalException)
        {
            return null;
        }
    }

    private static ImageSource? LoadThumbnail(string absolutePath, int targetPixelWidth)
    {
        try
        {
            using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = targetPixelWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                NotSupportedException or
                FileFormatException or
                ArgumentException or
                InvalidOperationException or
                ExternalException)
        {
            return null;
        }
    }

    private static VisualCacheKey CreateKey(
        VisualKind kind,
        DesktopFile file,
        string absolutePath,
        int targetPixelWidth) =>
        new(
            kind,
            Path.GetFullPath(absolutePath).ToUpperInvariant(),
            file.Length,
            file.LastWriteTimeUtc.UtcTicks,
            targetPixelWidth);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        nint bindContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? imageFactory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint objectHandle);

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            NativeSize size,
            ShellItemImageFactoryFlags flags,
            out nint bitmapHandle);
    }

    [Flags]
    private enum ShellItemImageFactoryFlags : uint
    {
        BiggerSizeOk = 0x00000001,
        IconOnly = 0x00000004,
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    private readonly record struct VisualCacheKey(
        VisualKind Kind,
        string Path,
        long Length,
        long LastWriteUtcTicks,
        int TargetPixelWidth);

    private enum VisualKind
    {
        ShellIcon,
        Thumbnail,
    }
}
#endif
