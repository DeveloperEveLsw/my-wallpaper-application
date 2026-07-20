using System.ComponentModel;
using System.Diagnostics;
using Wallpaper.Core.FileOperations;
using Wallpaper.Core.Naming;

namespace Wallpaper.Infrastructure.Windows.FileOperations;

public sealed class WindowsFileCommandService : IFileCommandService
{
    public Task EnsureValidAsync(FileCommandTarget target, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = FileCommandTargetValidator.ValidateExisting(target);
            },
            cancellationToken);

    public Task OpenAsync(FileCommandTarget target, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var validated = FileCommandTargetValidator.ValidateExisting(target);

                try
                {
                    var process = Process.Start(new ProcessStartInfo(validated.AbsolutePath)
                    {
                        UseShellExecute = true,
                    });
                    if (process is null)
                    {
                        throw new FileCommandException(
                            FileCommandError.OpenFailed,
                            "Windows에서 선택한 항목을 열지 못했습니다.");
                    }
                }
                catch (FileCommandException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or IOException)
                {
                    throw new FileCommandException(
                        FileCommandError.OpenFailed,
                        "Windows 기본 연결 프로그램으로 파일을 열지 못했습니다.",
                        exception);
                }
            },
            cancellationToken);

    public Task ShowInExplorerAsync(
        FileCommandTarget target,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows())
                {
                    throw new FileCommandException(
                        FileCommandError.ExplorerFailed,
                        "탐색기에서 위치 열기는 Windows에서만 사용할 수 있습니다.");
                }

                var validated = FileCommandTargetValidator.ValidateExisting(target);
                try
                {
                    var arguments = $"/select,\"{validated.AbsolutePath}\"";
                    var process = Process.Start(new ProcessStartInfo("explorer.exe", arguments)
                    {
                        UseShellExecute = true,
                    });
                    if (process is null)
                    {
                        throw new FileCommandException(
                            FileCommandError.ExplorerFailed,
                            "탐색기에서 선택한 항목의 위치를 열지 못했습니다.");
                    }
                }
                catch (FileCommandException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or IOException)
                {
                    throw new FileCommandException(
                        FileCommandError.ExplorerFailed,
                        "탐색기에서 선택한 항목의 위치를 열지 못했습니다.",
                        exception);
                }
            },
            cancellationToken);

    public Task<FileCommandTarget> RenameAsync(
        FileCommandTarget target,
        string newName,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => RenameCore(target, newName, cancellationToken),
            cancellationToken);

    public Task RecycleAsync(FileCommandTarget target, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new FileCommandException(
                FileCommandError.RecycleFailed,
                "휴지통 이동은 Windows에서만 사용할 수 있습니다.");
        }

        return FileOperationRecycleBin.RecycleAsync(target, cancellationToken);
    }

    private static FileCommandTarget RenameCore(
        FileCommandTarget target,
        string newName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validated = FileCommandTargetValidator.ValidateExisting(target);
        var nameValidation = WindowsFileNameValidator.Validate(newName);
        if (!nameValidation.IsValid)
        {
            throw new FileCommandException(
                FileCommandError.InvalidName,
                CreateInvalidNameMessage(nameValidation.Error));
        }

        if (string.Equals(validated.Name, newName, StringComparison.Ordinal))
        {
            throw new FileCommandException(
                FileCommandError.NoChange,
                "현재 이름과 동일합니다.");
        }

        var destinationPath = Path.Combine(validated.ParentPath, newName);
        string? collision;
        try
        {
            collision = Directory
                .EnumerateFileSystemEntries(validated.ParentPath)
                .FirstOrDefault(path =>
                    !string.Equals(path, validated.AbsolutePath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFileName(path), newName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new FileCommandException(
                FileCommandError.RenameFailed,
                "이름 충돌을 확인할 수 없습니다. 폴더 권한을 확인해 주세요.",
                exception);
        }

        if (collision is not null)
        {
            throw new FileCommandException(
                FileCommandError.NameCollision,
                "같은 위치에 동일한 이름의 항목이 이미 있습니다.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target.Kind == FileCommandItemKind.File)
            {
                File.Move(validated.AbsolutePath, destinationPath);
            }
            else
            {
                Directory.Move(validated.AbsolutePath, destinationPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new FileCommandException(
                FileCommandError.RenameFailed,
                "이름을 변경하지 못했습니다. 파일 잠금과 권한을 확인해 주세요.",
                exception);
        }

        var relativeParent = Path.GetDirectoryName(target.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var newRelativePath = string.IsNullOrEmpty(relativeParent)
            ? newName
            : Path.Combine(relativeParent, newName);
        var renamedTarget = target with
        {
            RelativePath = newRelativePath.Replace(Path.DirectorySeparatorChar, '/'),
        };

        _ = FileCommandTargetValidator.ValidateExisting(renamedTarget);
        return renamedTarget;
    }

    public static string CreateInvalidNameMessage(FileNameError? error) => error switch
    {
        FileNameError.Empty => "이름을 입력해 주세요.",
        FileNameError.RelativeSegment => "점(.)만으로 된 상대 경로 이름은 사용할 수 없습니다.",
        FileNameError.TrailingSpaceOrPeriod => "이름 끝에는 공백이나 마침표를 사용할 수 없습니다.",
        FileNameError.InvalidCharacter => "Windows 이름에 사용할 수 없는 문자가 포함되어 있습니다.",
        FileNameError.ReservedDeviceName => "Windows 예약 장치 이름은 사용할 수 없습니다.",
        FileNameError.TooLong => "이름은 255자를 넘을 수 없습니다.",
        _ => "Windows에서 사용할 수 없는 이름입니다.",
    };
}
