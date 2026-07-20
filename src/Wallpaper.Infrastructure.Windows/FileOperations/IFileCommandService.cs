using Wallpaper.Core.FileOperations;

namespace Wallpaper.Infrastructure.Windows.FileOperations;

public interface IFileCommandService
{
    Task EnsureValidAsync(FileCommandTarget target, CancellationToken cancellationToken = default);

    Task OpenAsync(FileCommandTarget target, CancellationToken cancellationToken = default);

    Task ShowInExplorerAsync(FileCommandTarget target, CancellationToken cancellationToken = default);

    Task<FileCommandTarget> RenameAsync(
        FileCommandTarget target,
        string newName,
        CancellationToken cancellationToken = default);

    Task RecycleAsync(FileCommandTarget target, CancellationToken cancellationToken = default);
}
