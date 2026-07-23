using System.IO.Pipes;
using System.Text.Json;

namespace Wallpaper.Seelen.M0.Companion;

internal sealed class BootstrapPipe(SessionRegistry sessions, int port)
{
    public const string PipeName = "wallpaper-seelen-m0-companion-v1";
    private const int MaximumMessageCharacters = 1024;

    public async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await pipe.WaitForConnectionAsync(cancellationToken);
            using var connectionTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectionTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await HandleConnectionAsync(pipe, connectionTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Drop an incomplete same-user handoff and accept the next one.
            }
        }
    }

    public static async Task<bool> ForwardToPrimaryAsync(
        CompanionOptions options,
        CancellationToken cancellationToken)
    {
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(5);
        while (TimeProvider.System.GetUtcNow() < deadline
               && !cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.ConnectAsync(500, cancellationToken);
                using var writer = new StreamWriter(pipe, leaveOpen: true)
                {
                    AutoFlush = true,
                };
                using var reader = new StreamReader(pipe, leaveOpen: true);

                var request = new BootstrapPipeRequest(
                    options.BootstrapNonce.Encoded,
                    options.Origin);
                await writer.WriteLineAsync(
                    JsonSerializer.Serialize(request).AsMemory(),
                    cancellationToken);

                var responseLine = await reader.ReadLineAsync(cancellationToken);
                var response = responseLine is null
                    ? null
                    : JsonSerializer.Deserialize<BootstrapPipeResponse>(responseLine);
                return response?.Accepted == true;
            }
            catch (Exception exception) when (
                exception is IOException
                    or TimeoutException)
            {
                await Task.Delay(200, cancellationToken);
            }
        }

        return false;
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true)
        {
            AutoFlush = true,
        };

        BootstrapPipeResponse response;
        try
        {
            var requestLine = await reader.ReadLineAsync(cancellationToken);
            var request = requestLine is null || requestLine.Length > MaximumMessageCharacters
                ? null
                : JsonSerializer.Deserialize<BootstrapPipeRequest>(requestLine);

            if (request is null
                || !OriginPolicy.IsAllowed(request.Origin)
                || !BootstrapNonce.TryParse(request.BootstrapNonce, out var nonce))
            {
                response = new BootstrapPipeResponse(false, port, "invalid_bootstrap");
            }
            else
            {
                sessions.RegisterBootstrap(nonce!, request.Origin);
                response = new BootstrapPipeResponse(true, port, null);
            }
        }
        catch (JsonException)
        {
            response = new BootstrapPipeResponse(false, port, "invalid_json");
        }

        await writer.WriteLineAsync(
            JsonSerializer.Serialize(response).AsMemory(),
            cancellationToken);
    }
}

internal sealed record BootstrapPipeRequest(string BootstrapNonce, string Origin);

internal sealed record BootstrapPipeResponse(bool Accepted, int Port, string? Error);
