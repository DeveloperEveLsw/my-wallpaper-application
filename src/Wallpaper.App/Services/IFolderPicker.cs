namespace Wallpaper.App.Services;

public interface IFolderPicker
{
    string? PickFolder(string? initialDirectory);
}
