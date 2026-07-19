using System.IO;
using Microsoft.Win32;

namespace Wallpaper.App.Services;

public sealed class WindowsFolderPicker : IFolderPicker
{
    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "월페이퍼 루트 폴더 선택",
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
