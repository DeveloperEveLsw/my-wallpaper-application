using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace Wallpaper.Seelen.Companion;

internal sealed class ShellMenuPipeServer(
    ShellMenuTicketRegistry tickets,
    DesktopCommandService commandService)
{
    internal const string PipeName = "wallpaper-seelen-shell-menu-v1";
    private const int MaximumRequestCharacters = 2048;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(cancellationToken);
            try
            {
                await HandleConnectionAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Drop a broker that did not redeem its ticket within the handshake timeout.
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or InvalidOperationException)
            {
                // A disconnected or malformed same-user broker must not stop the product server.
            }
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var handshakeTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(5));

        var line = await reader.ReadLineAsync(handshakeTimeout.Token);
        var request = line is null || line.Length > MaximumRequestCharacters
            ? null
            : JsonSerializer.Deserialize<ShellMenuPipeRedeemRequest>(line, JsonOptions);
        if (request is null
            || string.IsNullOrWhiteSpace(request.Ticket)
            || !tickets.TryRedeem(request.Ticket, out var launch)
            || launch is null)
        {
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(
                    new ShellMenuPipeLaunchResponse(
                        false,
                        "shell_menu_ticket_rejected",
                        "Windows 추가 옵션 메뉴 요청을 확인할 수 없습니다.",
                        null,
                        null,
                        null,
                        null,
                        0,
                        0,
                        0),
                    JsonOptions));
            return;
        }

        ShellMenuCompletion completion;
        try
        {
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(
                    new ShellMenuPipeLaunchResponse(
                        true,
                        null,
                        null,
                        launch.RequestId,
                        launch.Target.RootPath,
                        launch.Target.RelativePath,
                        launch.Target.Kind.ToString(),
                        launch.ScreenX,
                        launch.ScreenY,
                        launch.OwnerWindow),
                    JsonOptions));

            var completionLine = await reader.ReadLineAsync(cancellationToken);
            var brokerCompletion =
                completionLine is null || completionLine.Length > MaximumRequestCharacters
                    ? null
                    : JsonSerializer.Deserialize<ShellMenuPipeCompletionRequest>(
                        completionLine,
                        JsonOptions);
            completion = brokerCompletion is not null
                && string.Equals(
                    brokerCompletion.RequestId,
                    launch.RequestId,
                    StringComparison.Ordinal)
                    ? new ShellMenuCompletion(
                        launch.RequestId,
                        brokerCompletion.Succeeded,
                        brokerCompletion.CommandInvoked,
                        brokerCompletion.Code,
                        brokerCompletion.Message)
                    : new ShellMenuCompletion(
                        launch.RequestId,
                        Succeeded: false,
                        CommandInvoked: false,
                        "shell_menu_broker_disconnected",
                        "Windows 추가 옵션 메뉴 호스트의 연결이 끊겼습니다.");
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or InvalidOperationException)
        {
            completion = new ShellMenuCompletion(
                launch.RequestId,
                Succeeded: false,
                CommandInvoked: false,
                "shell_menu_broker_failed",
                "Windows 추가 옵션 메뉴 호스트가 작업을 완료하지 못했습니다.");
        }

        try
        {
            await commandService.RefreshProjectionAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            completion = completion.Succeeded
                ? completion with
                {
                    Code = "shell_menu_refresh_failed",
                    Message = "Windows 명령은 끝났지만 화면을 다시 불러오지 못했습니다.",
                }
                : completion;
        }

        tickets.Complete(launch.Ticket, completion);
    }
}
