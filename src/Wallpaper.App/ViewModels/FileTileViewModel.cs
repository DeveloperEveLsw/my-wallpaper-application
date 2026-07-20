using System.IO;
using System.Windows.Media;
using Wallpaper.Core.Models;
using Wallpaper.Infrastructure.Windows.Visuals;

namespace Wallpaper.App.ViewModels;

public sealed class FileTileViewModel(
    DesktopFile file,
    string absolutePath,
    IFileVisualService visualService) : ObservableObject
{
    private readonly string _absolutePath = absolutePath;
    private readonly IFileVisualService _visualService = visualService;
    private readonly SemaphoreSlim _visualLoadGate = new(1, 1);
    private ImageSource? _visual;
    private int _loadedPixelWidth;
    private bool _visualIsThumbnail;

    public DesktopFile File { get; } = file;

    public string Name => File.Name;

    public ImageSource? Visual
    {
        get => _visual;
        private set => SetProperty(ref _visual, value);
    }

    public string ExtensionLabel
    {
        get
        {
            var extension = File.Extension.TrimStart('.');
            if (string.IsNullOrWhiteSpace(extension))
            {
                return "FILE";
            }

            return extension.Length <= 5
                ? extension.ToUpperInvariant()
                : extension[..5].ToUpperInvariant();
        }
    }

    public async Task EnsureVisualLoadedAsync(
        int targetPixelWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetPixelWidth);

        if (_loadedPixelWidth >= targetPixelWidth)
        {
            return;
        }

        await _visualLoadGate.WaitAsync(cancellationToken);
        try
        {
            if (_loadedPixelWidth >= targetPixelWidth)
            {
                return;
            }

            var shellIcon = await _visualService.LoadShellIconAsync(
                File,
                _absolutePath,
                targetPixelWidth,
                cancellationToken);
            if (shellIcon is not null && !_visualIsThumbnail)
            {
                Visual = shellIcon;
            }

            var thumbnail = await _visualService.LoadThumbnailAsync(
                File,
                _absolutePath,
                targetPixelWidth,
                cancellationToken);
            if (thumbnail is not null)
            {
                Visual = thumbnail;
                _visualIsThumbnail = true;
            }

            _loadedPixelWidth = targetPixelWidth;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException or
                OperationCanceledException)
        {
            if (exception is not OperationCanceledException)
            {
                _loadedPixelWidth = targetPixelWidth;
            }
        }
        finally
        {
            _visualLoadGate.Release();
        }
    }
}
