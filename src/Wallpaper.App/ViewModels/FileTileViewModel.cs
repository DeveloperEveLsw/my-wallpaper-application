using System.IO;
using System.Windows.Media.Imaging;
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
    private BitmapSource? _visual;
    private string _name = ShortcutDisplayNamePolicy.CreateFallback(file.Name, file.Extension);
    private int _loadedPixelWidth;
    private FileVisualKind? _visualKind;
    private FileVisualPresentation _visualPresentation;
    private bool _isSelected;

    public DesktopFile File { get; } = file;

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public string OriginalName => File.Name;

    public string RelativePath => File.RelativePath;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public BitmapSource? Visual
    {
        get => _visual;
        private set => SetProperty(ref _visual, value);
    }

    public FileVisualPresentation VisualPresentation
    {
        get => _visualPresentation;
        private set => SetProperty(ref _visualPresentation, value);
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
            if (shellIcon is not null && _visualKind is not FileVisualKind.Thumbnail)
            {
                ApplyVisual(shellIcon);
            }

            var thumbnail = await _visualService.LoadThumbnailAsync(
                File,
                _absolutePath,
                targetPixelWidth,
                cancellationToken);
            if (thumbnail is not null)
            {
                ApplyVisual(thumbnail);
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

    private void ApplyVisual(FileVisualResult visual)
    {
        if (visual.Kind == FileVisualKind.ShellIcon)
        {
            Name = ShortcutDisplayNamePolicy.NormalizeShellDisplayName(
                visual.DisplayName,
                File.Name,
                File.Extension);
        }

        _visualKind = visual.Kind;
        VisualPresentation = visual.Presentation;
        Visual = visual.Source;
    }
}
