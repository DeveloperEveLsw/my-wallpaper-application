using System.IO.Pipes;
using System.IO;
using System.Text.Json;
namespace Wallpaper.Seelen.Companion;

internal sealed class ProductBootstrapPipe(SessionRegistry sessions, int port)
{
    private const string PipeName = "wallpaper-seelen-companion-v1";
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await HandleConnectionAsync(pipe, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Drop an incomplete same-user handoff.
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
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await writer.WriteLineAsync(
                    JsonSerializer.Serialize(
                        new PipeRequest(options.BootstrapNonce.Encoded, options.Origin)).AsMemory(),
                    cancellationToken);
                var line = await reader.ReadLineAsync(cancellationToken);
                return line is not null
                    && JsonSerializer.Deserialize<PipeResponse>(line)?.Accepted == true;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException)
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
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        PipeResponse response;
        try
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            var request = line is null || line.Length > MaximumMessageCharacters
                ? null
                : JsonSerializer.Deserialize<PipeRequest>(line);
            if (request is null
                || !OriginPolicy.IsAllowed(request.Origin)
                || !BootstrapNonce.TryParse(request.BootstrapNonce, out var nonce))
            {
                response = new PipeResponse(false, port);
            }
            else
            {
                sessions.RegisterBootstrap(nonce!, request.Origin);
                response = new PipeResponse(true, port);
            }
        }
        catch (JsonException)
        {
            response = new PipeResponse(false, port);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), cancellationToken);
    }

    private sealed record PipeRequest(string BootstrapNonce, string Origin);

    private sealed record PipeResponse(bool Accepted, int Port);
}
