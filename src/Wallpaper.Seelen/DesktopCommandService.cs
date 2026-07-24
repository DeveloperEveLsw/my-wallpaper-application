using Wallpaper.Core.FileOperations;
using Wallpaper.Core.Models;
using Wallpaper.Infrastructure.Windows.FileOperations;

namespace Wallpaper.Seelen;

public sealed class DesktopCommandService
{
    private const int MaximumCachedRequests = 256;

    private readonly object _requestGate = new();
    private readonly DesktopProjectionService _projection;
    private readonly IFileCommandService _fileCommands;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly Dictionary<string, CachedRequest> _requests = new(StringComparer.Ordinal);
    private readonly Queue<string> _requestOrder = new();

    public DesktopCommandService(
        DesktopProjectionService projection,
        IFileCommandService fileCommands)
    {
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _fileCommands = fileCommands ?? throw new ArgumentNullException(nameof(fileCommands));
    }

    public Task<DesktopCommandResult> ExecuteAsync(
        DesktopCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_requestGate)
        {
            if (_requests.TryGetValue(request.RequestId, out var existing))
            {
                return existing.Request == request
                    ? existing.Task
                    : Task.FromResult(
                        DesktopCommandResult.Failure(
                            request,
                            "duplicate_request",
                            "같은 requestId를 다른 명령에 다시 사용할 수 없습니다."));
            }

            var completion = new TaskCompletionSource<DesktopCommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Add(request.RequestId, new CachedRequest(request, completion.Task));
            _requestOrder.Enqueue(request.RequestId);
            _ = ExecuteAndCompleteAsync(request, completion, cancellationToken);
            return completion.Task;
        }
    }

    public async Task RefreshProjectionAsync(CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _projection.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<bool> SetFolderOrderAsync(
        IReadOnlyList<string> orderedIds,
        CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _projection.SetFolderOrderAsync(orderedIds, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<bool> SetRootPathAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _projection.SetRootPathAsync(rootPath, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<bool> UseDefaultRootAsync(CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _projection.UseDefaultRootAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<DesktopShellMenuTargetResult> PrepareShellMenuTargetAsync(
        DesktopShellMenuRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_projection.TryGetItem(request.ItemId, out var item) || item is null)
            {
                await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
                return DesktopShellMenuTargetResult.Failure(
                    "target_missing",
                    "선택한 항목이 더 이상 현재 목록에 없습니다.");
            }

            var target = ToFileCommandTarget(item);
            try
            {
                await _fileCommands.EnsureValidAsync(target, cancellationToken)
                    .ConfigureAwait(false);
                return DesktopShellMenuTargetResult.Success(target);
            }
            catch (FileCommandException exception)
            {
                await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
                return DesktopShellMenuTargetResult.Failure(
                    exception.Error.ToString(),
                    exception.Message);
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task ExecuteAndCompleteAsync(
        DesktopCommandRequest request,
        TaskCompletionSource<DesktopCommandResult> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            completion.TrySetResult(await ExecuteSerializedAsync(request, cancellationToken));
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception)
        {
            completion.TrySetResult(
                DesktopCommandResult.Failure(
                    request,
                    "internal_error",
                    "파일 명령을 처리하는 중 예기치 않은 오류가 발생했습니다."));
        }
        finally
        {
            TrimCompletedRequests();
        }
    }

    private async Task<DesktopCommandResult> ExecuteSerializedAsync(
        DesktopCommandRequest request,
        CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return request.Action switch
            {
                DesktopCommandActions.Open =>
                    await ExecuteOpenAsync(request, cancellationToken).ConfigureAwait(false),
                DesktopCommandActions.ShowInExplorer =>
                    await ExecuteShowInExplorerAsync(request, cancellationToken).ConfigureAwait(false),
                DesktopCommandActions.Rename =>
                    await ExecuteRenameAsync(request, cancellationToken).ConfigureAwait(false),
                DesktopCommandActions.Recycle =>
                    await ExecuteRecycleAsync(request, cancellationToken).ConfigureAwait(false),
                DesktopCommandActions.PrepareMove =>
                    await ExecutePrepareMoveAsync(request, cancellationToken).ConfigureAwait(false),
                DesktopCommandActions.Move =>
                    await ExecuteMoveAsync(request, cancellationToken).ConfigureAwait(false),
                _ => DesktopCommandResult.Failure(
                    request,
                    "invalid_action",
                    "지원하지 않는 파일 명령입니다."),
            };
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task<DesktopCommandResult> ExecuteOpenAsync(
        DesktopCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveItem(request, out var item, out var failure))
        {
            await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            return failure!;
        }

        try
        {
            await _fileCommands.OpenAsync(ToFileCommandTarget(item!), cancellationToken)
                .ConfigureAwait(false);
            return DesktopCommandResult.Success(request);
        }
        catch (FileCommandException exception)
        {
            await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            return FromFileCommandFailure(request, exception);
        }
    }

    private async Task<DesktopCommandResult> ExecuteShowInExplorerAsync(
        DesktopCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveItem(request, out var item, out var failure))
        {
            await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            return failure!;
        }

        try
        {
            await _fileCommands.ShowInExplorerAsync(ToFileCommandTarget(item!), cancellationToken)
                .ConfigureAwait(false);
            return DesktopCommandResult.Success(request);
        }
        catch (FileCommandException exception)
        {
            await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            return FromFileCommandFailure(request, exception);
        }
    }

    private async Task<DesktopCommandResult> ExecuteRenameAsync(
        DesktopCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveItem(request, out var item, out var failure))
        {
            await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            return failure!;
        }

        var previousOrder = _projection.Current.Folders.Select(folder => folder.Id).ToArray();
        DesktopCommandResult result;
        try
        {
            var renamed = await _fileCommands.RenameAsync(
                    ToFileCommandTarget(item!),
                    request.NewName ?? string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);
            result = DesktopCommandResult.Success(request);
            if (item!.Kind == FileCommandItemKind.Folder)
            {
                try
                {
                    await _projection.ChangeFolderIdentityAsync(
                            item.Id,
                            DesktopItemId.ForFolder(renamed.RelativePath),
                            previousOrder,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = DesktopCommandResult.Success(
                        request,
                        code: "settings_update_failed",
                        message: "이름은 변경했지만 Dock 순서 설정을 저장하지 못했습니다.");
                }
            }
        }
        catch (FileCommandException exception)
        {
            result = FromFileCommandFailure(request, exception);
        }

        return await RefreshAfterMutationAsync(request, result, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DesktopCommandResult> ExecuteRecycleAsync(
        DesktopCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveItem(request, out var item, out var failure))
        {
            await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            return failure!;
        }

        var previousOrder = _projection.Current.Folders.Select(folder => folder.Id).ToArray();
        DesktopCommandResult result;
        try
        {
            await _fileCommands.RecycleAsync(ToFileCommandTarget(item!), cancellationToken)
                .ConfigureAwait(false);
            result = DesktopCommandResult.Success(request);
            if (item!.Kind == FileCommandItemKind.Folder)
            {
                try
                {
                    await _projection.ChangeFolderIdentityAsync(
                            item.Id,
                            replacementId: null,
                            previousOrder,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = DesktopCommandResult.Success(
                        request,
                        code: "settings_update_failed",
                        message: "폴더는 휴지통으로 이동했지만 Dock 순서 설정을 저장하지 못했습니다.");
                }
            }
        }
        catch (FileCommandException exception)
        {
            result = FromFileCommandFailure(request, exception);
        }

        return await RefreshAfterMutationAsync(request, result, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DesktopCommandResult> ExecutePrepareMoveAsync(
        DesktopCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveMoveEndpoints(request, out var item, out var destination, out var failure))
        {
            await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            return failure!;
        }

        try
        {
            var preparation = await _fileCommands.PrepareMoveAsync(
                    ToFileCommandTarget(item!),
                    ToMoveDestination(destination!),
                    request.NewName ?? item!.Name,
                    cancellationToken)
                .ConfigureAwait(false);
            return DesktopCommandResult.Success(
                request,
                preparation.ProposedName,
                preparation.HasNameCollision);
        }
        catch (FileCommandException exception)
        {
            await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            return FromFileCommandFailure(request, exception);
        }
    }

    private async Task<DesktopCommandResult> ExecuteMoveAsync(
        DesktopCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveMoveEndpoints(request, out var item, out var destination, out var failure))
        {
            await RefreshAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            return failure!;
        }

        DesktopCommandResult result;
        try
        {
            await _fileCommands.MoveAsync(
                    ToFileCommandTarget(item!),
                    ToMoveDestination(destination!),
                    request.NewName ?? string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);
            result = DesktopCommandResult.Success(request);
        }
        catch (FileCommandException exception)
        {
            result = FromFileCommandFailure(request, exception);
        }

        return await RefreshAfterMutationAsync(request, result, cancellationToken)
            .ConfigureAwait(false);
    }

    private bool TryResolveItem(
        DesktopCommandRequest request,
        out ProjectionItemTarget? item,
        out DesktopCommandResult? failure)
    {
        if (!_projection.TryGetItem(request.ItemId, out item) || item is null)
        {
            failure = DesktopCommandResult.Failure(
                request,
                "target_missing",
                "선택한 항목이 더 이상 현재 목록에 없습니다.");
            return false;
        }

        failure = null;
        return true;
    }

    private bool TryResolveMoveEndpoints(
        DesktopCommandRequest request,
        out ProjectionItemTarget? item,
        out ProjectionMoveDestinationTarget? destination,
        out DesktopCommandResult? failure)
    {
        destination = null;
        if (!TryResolveItem(request, out item, out failure))
        {
            return false;
        }

        if (item!.Kind != FileCommandItemKind.File)
        {
            failure = DesktopCommandResult.Failure(
                request,
                "invalid_target",
                "M4에서는 파일만 카드 사이에서 이동할 수 있습니다.");
            return false;
        }

        if (!_projection.TryGetMoveDestination(request.DestinationId ?? string.Empty, out destination)
            || destination is null)
        {
            failure = DesktopCommandResult.Failure(
                request,
                "destination_missing",
                "이동할 폴더가 더 이상 현재 목록에 없습니다.");
            return false;
        }

        failure = null;
        return true;
    }

    private async Task<DesktopCommandResult> RefreshAfterMutationAsync(
        DesktopCommandRequest request,
        DesktopCommandResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await _projection.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return result.Accepted
                ? DesktopCommandResult.Success(
                    request,
                    code: "refresh_failed",
                    message: "파일 작업은 완료했지만 화면을 다시 불러오지 못했습니다.")
                : result;
        }
    }

    private async Task RefreshAfterFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _projection.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Preserve the actionable command error when recovery scanning also fails.
        }
    }

    private static FileCommandTarget ToFileCommandTarget(ProjectionItemTarget target) =>
        new(target.RootPath, target.RelativePath, target.Kind);

    private static FileMoveDestination ToMoveDestination(
        ProjectionMoveDestinationTarget destination) =>
        new(destination.RootPath, destination.RelativeFolderPath);

    private static DesktopCommandResult FromFileCommandFailure(
        DesktopCommandRequest request,
        FileCommandException exception) =>
        DesktopCommandResult.Failure(
            request,
            exception.Error.ToString(),
            exception.Message);

    private void TrimCompletedRequests()
    {
        lock (_requestGate)
        {
            while (_requests.Count > MaximumCachedRequests && _requestOrder.Count > 0)
            {
                var requestId = _requestOrder.Peek();
                if (_requests.TryGetValue(requestId, out var request) && !request.Task.IsCompleted)
                {
                    break;
                }

                _requestOrder.Dequeue();
                _requests.Remove(requestId);
            }
        }
    }

    private sealed record CachedRequest(
        DesktopCommandRequest Request,
        Task<DesktopCommandResult> Task);
}
