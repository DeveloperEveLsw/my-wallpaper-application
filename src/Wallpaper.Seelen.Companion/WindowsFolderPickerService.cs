using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Wallpaper.Seelen.Companion;

internal sealed class WindowsFolderPickerService
{
    private readonly SemaphoreSlim _singlePicker = new(1, 1);

    public async Task<string?> PickAsync(
        string? initialDirectory,
        CancellationToken cancellationToken)
    {
        await _singlePicker.WaitAsync(cancellationToken);
        try
        {
            var completion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    var dialog = new OpenFolderDialog
                    {
                        Title = "월페이퍼 루트 폴더 선택",
                        Multiselect = false,
                    };
                    if (!string.IsNullOrWhiteSpace(initialDirectory)
                        && Directory.Exists(initialDirectory))
                    {
                        dialog.InitialDirectory = initialDirectory;
                    }

                    var owner = new Window
                    {
                        Height = 1,
                        Left = -10000,
                        Opacity = 0,
                        ShowInTaskbar = false,
                        Top = -10000,
                        Topmost = true,
                        Width = 1,
                        WindowStyle = WindowStyle.None,
                    };
                    try
                    {
                        owner.Show();
                        completion.TrySetResult(
                            dialog.ShowDialog(owner) == true ? dialog.FolderName : null);
                    }
                    finally
                    {
                        owner.Close();
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            })
            {
                IsBackground = true,
                Name = "Wallpaper folder picker",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _singlePicker.Release();
        }
    }
}
