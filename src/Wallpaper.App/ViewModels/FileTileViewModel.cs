using Wallpaper.Core.Models;

namespace Wallpaper.App.ViewModels;

public sealed class FileTileViewModel(DesktopFile file)
{
    public DesktopFile File { get; } = file;

    public string Name => File.Name;

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
}
