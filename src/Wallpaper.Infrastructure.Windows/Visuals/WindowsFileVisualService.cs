#if WINDOWS
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wallpaper.Core.Models;

namespace Wallpaper.Infrastructure.Windows.Visuals;

public sealed class WindowsFileVisualService : IFileVisualService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiDisplayName = 0x000000200;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiSystemIconIndex = 0x000004000;
    private const int ImageListDrawTransparent = 0x00000001;
    private const int DefaultCacheCapacity = 512;

    private static readonly HashSet<string> ImageExtensions = new(
        [".bmp", ".dib", ".gif", ".heic", ".heif", ".jfif", ".jpe", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"],
        StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<VisualCacheKey, Lazy<Task<FileVisualResult?>>> _cache = new();
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

    public async Task<FileVisualResult?> LoadShellIconAsync(
        DesktopFile file,
        string absolutePath,
        int targetPixelWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetPixelWidth);

        var shortcutIcon = string.Equals(file.Extension, ".url", StringComparison.OrdinalIgnoreCase)
            ? await Task.Run(
                () => ResolveInternetShortcutIcon(absolutePath),
                cancellationToken).ConfigureAwait(false)
            : null;
        var key = CreateKey(
            VisualKind.ShellIcon,
            file,
            absolutePath,
            targetPixelWidth,
            shortcutIcon);
        return await GetOrLoadAsync(
            key,
            () => CreateVisualResult(
                LoadInternetShortcutIcon(shortcutIcon, targetPixelWidth) ??
                    LoadShellIcon(absolutePath, targetPixelWidth),
                FileVisualKind.ShellIcon,
                LoadShellDisplayName(absolutePath, file.Extension)),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<FileVisualResult?> LoadThumbnailAsync(
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
            return Task.FromResult<FileVisualResult?>(null);
        }

        var key = CreateKey(VisualKind.Thumbnail, file, absolutePath, targetPixelWidth);
        return GetOrLoadAsync(
            key,
            () => CreateVisualResult(
                LoadThumbnail(absolutePath, targetPixelWidth),
                FileVisualKind.Thumbnail),
            cancellationToken);
    }

    private async Task<FileVisualResult?> GetOrLoadAsync(
        VisualCacheKey key,
        Func<FileVisualResult?> loader,
        CancellationToken cancellationToken)
    {
        var candidate = new Lazy<Task<FileVisualResult?>>(
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

    private static BitmapSource? LoadShellIcon(string absolutePath, int targetPixelWidth)
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
                return LoadLegacyShellIcon(absolutePath, targetPixelWidth);
            }

            var imageResult = imageFactory.GetImage(
                new NativeSize(targetPixelWidth, targetPixelWidth),
                ShellItemImageFactoryFlags.BiggerSizeOk | ShellItemImageFactoryFlags.IconOnly,
                out bitmapHandle);
            if (imageResult < 0 || bitmapHandle == 0)
            {
                return LoadLegacyShellIcon(absolutePath, targetPixelWidth);
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
            return LoadLegacyShellIcon(absolutePath, targetPixelWidth);
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

    private static BitmapSource? LoadLegacyShellIcon(string absolutePath, int targetPixelWidth) =>
        LoadSystemImageListIcon(absolutePath, targetPixelWidth) ?? LoadClassicShellIcon(absolutePath);

    private static BitmapSource? LoadSystemImageListIcon(string absolutePath, int targetPixelWidth)
    {
        IImageList? imageList = null;
        nint iconHandle = 0;
        try
        {
            var fileInfo = new ShellFileInfo();
            var fileInfoResult = SHGetFileInfo(
                absolutePath,
                fileAttributes: 0,
                ref fileInfo,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiSystemIconIndex);
            if (fileInfoResult == 0)
            {
                return null;
            }

            var interfaceId = typeof(IImageList).GUID;
            var imageListSize = targetPixelWidth > 48
                ? SystemImageListSize.Jumbo
                : SystemImageListSize.ExtraLarge;
            var imageListResult = SHGetImageList(
                imageListSize,
                ref interfaceId,
                out imageList);
            if (imageListResult < 0 || imageList is null)
            {
                return null;
            }

            var iconResult = imageList.GetIcon(
                fileInfo.IconIndex,
                ImageListDrawTransparent,
                out iconHandle);
            if (iconResult < 0 || iconHandle == 0)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                ExternalException or
                EntryPointNotFoundException or
                DllNotFoundException or
                ArgumentException or
                InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (iconHandle != 0)
            {
                _ = DestroyIcon(iconHandle);
            }

            if (imageList is not null && Marshal.IsComObject(imageList))
            {
                _ = Marshal.FinalReleaseComObject(imageList);
            }
        }
    }

    private static BitmapSource? LoadClassicShellIcon(string absolutePath)
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

    private static string? LoadShellDisplayName(string absolutePath, string extension)
    {
        if (!ShortcutDisplayNamePolicy.IsShortcutExtension(extension))
        {
            return null;
        }

        try
        {
            var fileInfo = new ShellFileInfo();
            var result = SHGetFileInfo(
                absolutePath,
                fileAttributes: 0,
                ref fileInfo,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiDisplayName);
            return result == 0 || string.IsNullOrWhiteSpace(fileInfo.DisplayName)
                ? null
                : fileInfo.DisplayName;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                ExternalException or
                ArgumentException)
        {
            return null;
        }
    }

    private static InternetShortcutIconSource? ResolveInternetShortcutIcon(string shortcutPath)
    {
        try
        {
            var iconFileValue = ReadInternetShortcutValue(shortcutPath, "IconFile");
            if (string.IsNullOrWhiteSpace(iconFileValue))
            {
                return null;
            }

            var expandedPath = Environment
                .ExpandEnvironmentVariables(iconFileValue)
                .Trim()
                .Trim('"');
            if (Uri.TryCreate(expandedPath, UriKind.Absolute, out var iconUri) && iconUri.IsFile)
            {
                expandedPath = iconUri.LocalPath;
            }

            if (!Path.IsPathFullyQualified(expandedPath))
            {
                var shortcutDirectory = Path.GetDirectoryName(shortcutPath);
                if (string.IsNullOrWhiteSpace(shortcutDirectory))
                {
                    return null;
                }

                expandedPath = Path.Combine(shortcutDirectory, expandedPath);
            }

            var normalizedPath = Path.GetFullPath(expandedPath);
            var iconFile = new FileInfo(normalizedPath);
            iconFile.Refresh();
            if (!iconFile.Exists)
            {
                return null;
            }

            var iconIndexValue = ReadInternetShortcutValue(shortcutPath, "IconIndex");
            var iconIndex = int.TryParse(
                iconIndexValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedIconIndex)
                ? parsedIconIndex
                : 0;
            return new InternetShortcutIconSource(
                normalizedPath,
                iconIndex,
                iconFile.Length,
                iconFile.LastWriteTimeUtc.Ticks);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                ArgumentException or
                NotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadInternetShortcutValue(string shortcutPath, string key)
    {
        const int bufferCapacity = 32 * 1024;
        var buffer = new StringBuilder(bufferCapacity);
        var length = GetPrivateProfileString(
            "InternetShortcut",
            key,
            defaultValue: null,
            buffer,
            buffer.Capacity,
            shortcutPath);
        return length == 0 ? null : buffer.ToString();
    }

    private static BitmapSource? LoadInternetShortcutIcon(
        InternetShortcutIconSource? shortcutIcon,
        int targetPixelWidth)
    {
        if (shortcutIcon is null)
        {
            return null;
        }

        return string.Equals(
            Path.GetExtension(shortcutIcon.Path),
            ".ico",
            StringComparison.OrdinalIgnoreCase)
            ? LoadIconFile(shortcutIcon.Path, targetPixelWidth) ??
                LoadIconResource(shortcutIcon, targetPixelWidth)
            : LoadIconResource(shortcutIcon, targetPixelWidth);
    }

    private static BitmapSource? LoadIconFile(string iconPath, int targetPixelWidth)
    {
        try
        {
            using var stream = new FileStream(
                iconPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 32 * 1024,
                FileOptions.SequentialScan);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames
                .OrderBy(candidate =>
                    Math.Min(candidate.PixelWidth, candidate.PixelHeight) >= targetPixelWidth ? 0 : 1)
                .ThenBy(candidate =>
                    Math.Abs(Math.Min(candidate.PixelWidth, candidate.PixelHeight) - targetPixelWidth))
                .FirstOrDefault();
            if (frame is null)
            {
                return null;
            }

            frame.Freeze();
            return frame;
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

    private static BitmapSource? LoadIconResource(
        InternetShortcutIconSource shortcutIcon,
        int targetPixelWidth)
    {
        nint iconHandle = 0;
        try
        {
            var extractedCount = PrivateExtractIcons(
                shortcutIcon.Path,
                shortcutIcon.IconIndex,
                targetPixelWidth,
                targetPixelWidth,
                out iconHandle,
                out _,
                iconCount: 1,
                flags: 0);
            if (extractedCount == 0 || extractedCount == uint.MaxValue || iconHandle == 0)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                ExternalException or
                ArgumentException or
                InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (iconHandle != 0)
            {
                _ = DestroyIcon(iconHandle);
            }
        }
    }

    private static BitmapSource? LoadThumbnail(string absolutePath, int targetPixelWidth)
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
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var decodeByWidth = frame.PixelWidth <= frame.PixelHeight;
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
            if (decodeByWidth)
            {
                image.DecodePixelWidth = targetPixelWidth;
            }
            else
            {
                image.DecodePixelHeight = targetPixelWidth;
            }

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

    private static FileVisualResult? CreateVisualResult(
        BitmapSource? source,
        FileVisualKind kind,
        string? displayName = null)
    {
        if (source is null)
        {
            return null;
        }

        var sourcePixelWidth = source.PixelWidth;
        var sourcePixelHeight = source.PixelHeight;
        var analysis = AnalyzeAlpha(source);
        var normalizedSource = analysis.Presentation == FileVisualPresentation.Contained
            ? CropTransparentPadding(source, analysis)
            : source;

        return new FileVisualResult(
            normalizedSource,
            kind,
            analysis.Presentation,
            sourcePixelWidth,
            sourcePixelHeight,
            displayName);
    }

    private static AlphaCoverageAnalysis AnalyzeAlpha(BitmapSource source)
    {
        var analysisSource = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, destinationPalette: null, alphaThreshold: 0);
        var stride = checked(analysisSource.PixelWidth * 4);
        var pixels = new byte[checked(stride * analysisSource.PixelHeight)];
        analysisSource.CopyPixels(pixels, stride, offset: 0);
        return FileVisualAlphaAnalyzer.Analyze(
            pixels,
            analysisSource.PixelWidth,
            analysisSource.PixelHeight,
            stride);
    }

    private static BitmapSource CropTransparentPadding(
        BitmapSource source,
        AlphaCoverageAnalysis analysis)
    {
        if (analysis.Left == 0 &&
            analysis.Top == 0 &&
            analysis.Width == source.PixelWidth &&
            analysis.Height == source.PixelHeight)
        {
            return source;
        }

        var cropped = new CroppedBitmap(
            source,
            new Int32Rect(analysis.Left, analysis.Top, analysis.Width, analysis.Height));
        cropped.Freeze();
        return cropped;
    }

    private static VisualCacheKey CreateKey(
        VisualKind kind,
        DesktopFile file,
        string absolutePath,
        int targetPixelWidth,
        InternetShortcutIconSource? shortcutIcon = null) =>
        new(
            kind,
            Path.GetFullPath(absolutePath).ToUpperInvariant(),
            file.Length,
            file.LastWriteTimeUtc.UtcTicks,
            targetPixelWidth,
            shortcutIcon?.Path.ToUpperInvariant() ?? string.Empty,
            shortcutIcon?.Length ?? 0,
            shortcutIcon?.LastWriteUtcTicks ?? 0,
            shortcutIcon?.IconIndex ?? 0);

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

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetImageList(
        SystemImageListSize imageList,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IImageList? imageListInterface);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileString(
        string appName,
        string keyName,
        string? defaultValue,
        StringBuilder returnedString,
        int size,
        string fileName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(
        string fileName,
        int iconIndex,
        int iconWidth,
        int iconHeight,
        out nint iconHandle,
        out uint iconId,
        uint iconCount,
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

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig]
        int Add(nint imageBitmap, nint maskBitmap, out int imageIndex);

        [PreserveSig]
        int ReplaceIcon(int imageIndex, nint iconHandle, out int replacementIndex);

        [PreserveSig]
        int SetOverlayImage(int imageIndex, int overlayIndex);

        [PreserveSig]
        int Replace(int imageIndex, nint imageBitmap, nint maskBitmap);

        [PreserveSig]
        int AddMasked(nint imageBitmap, uint maskColor, out int imageIndex);

        [PreserveSig]
        int Draw(nint drawParameters);

        [PreserveSig]
        int Remove(int imageIndex);

        [PreserveSig]
        int GetIcon(int imageIndex, int flags, out nint iconHandle);
    }

    [Flags]
    private enum ShellItemImageFactoryFlags : uint
    {
        BiggerSizeOk = 0x00000001,
        IconOnly = 0x00000004,
    }

    private enum SystemImageListSize
    {
        ExtraLarge = 2,
        Jumbo = 4,
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
        int TargetPixelWidth,
        string DependencyPath,
        long DependencyLength,
        long DependencyLastWriteUtcTicks,
        int DependencyIconIndex);

    private sealed record InternetShortcutIconSource(
        string Path,
        int IconIndex,
        long Length,
        long LastWriteUtcTicks);

    private enum VisualKind
    {
        ShellIcon,
        Thumbnail,
    }
}
#endif
