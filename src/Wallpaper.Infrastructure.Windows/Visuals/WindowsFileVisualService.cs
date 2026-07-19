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
    private const int ThumbnailDecodeWidth = 160;

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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        var key = CreateKey(VisualKind.ShellIcon, file, absolutePath);
        return GetOrLoadAsync(key, () => LoadShellIcon(absolutePath), cancellationToken);
    }

    public Task<ImageSource?> LoadThumbnailAsync(
        DesktopFile file,
        string absolutePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        if (!ImageExtensions.Contains(file.Extension))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        var key = CreateKey(VisualKind.Thumbnail, file, absolutePath);
        return GetOrLoadAsync(key, () => LoadThumbnail(absolutePath), cancellationToken);
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

    private static ImageSource? LoadShellIcon(string absolutePath)
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

    private static ImageSource? LoadThumbnail(string absolutePath)
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
            image.DecodePixelWidth = ThumbnailDecodeWidth;
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

    private static VisualCacheKey CreateKey(VisualKind kind, DesktopFile file, string absolutePath) =>
        new(
            kind,
            Path.GetFullPath(absolutePath).ToUpperInvariant(),
            file.Length,
            file.LastWriteTimeUtc.UtcTicks);

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
        long LastWriteUtcTicks);

    private enum VisualKind
    {
        ShellIcon,
        Thumbnail,
    }
}
#endif
