using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Primitives;
using Wallpaper.Core.FileOperations;
using Wallpaper.Infrastructure.Windows.FileOperations;

namespace Wallpaper.Seelen.Companion;

internal sealed class ProductLoopbackServer : IAsyncDisposable
{
    private const int ProtocolVersion = 3;
    private const int MaximumIncomingBytes = 256 * 1024;

    private ProductLoopbackServer(WebApplication application, int port)
    {
        Application = application;
        Port = port;
    }

    public WebApplication Application { get; }

    public int Port { get; }

    public static async Task<ProductLoopbackServer> StartAsync(
        CompanionOptions options,
        SessionRegistry sessions,
        DesktopProjectionService projection,
        WindowsVisualResponseService visuals,
        IFileCommandService fileCommands,
        CancellationToken cancellationToken)
    {
        foreach (var port in options.CandidatePorts)
        {
            if (!CanBindExclusively(port))
            {
                continue;
            }

            var application = BuildApplication(
                port,
                sessions,
                projection,
                visuals,
                fileCommands);
            try
            {
                await application.StartAsync(cancellationToken);
                return new ProductLoopbackServer(application, port);
            }
            catch (IOException)
            {
                await application.DisposeAsync();
            }
        }

        throw new IOException("No product loopback port in the allowlist could be bound.");
    }

    public async ValueTask DisposeAsync() => await Application.DisposeAsync();

    private static WebApplication BuildApplication(
        int port,
        SessionRegistry sessions,
        DesktopProjectionService projection,
        WindowsVisualResponseService visuals,
        IFileCommandService fileCommands)
    {
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(ProductLoopbackServer).Assembly.FullName,
                ContentRootPath = Path.GetTempPath(),
                EnvironmentName = Environments.Production,
            });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http1);
        });

        var application = builder.Build();
        var hostPolicy = new HostPolicy(port);
        application.Use(async (context, next) =>
        {
            var host = GetSingleHeader(context.Request.Headers, "Host");
            if (!hostPolicy.IsAllowed(host)
                || context.Connection.RemoteIpAddress is null
                || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await next(context);
        });
        application.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

        application.MapMethods(
            "/{**path}",
            ["OPTIONS"],
            async context =>
            {
                var origin = GetSingleHeader(context.Request.Headers, "Origin");
                if (!sessions.HasAllowedOrigin(origin))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                ApplyCorsHeaders(context.Response, context.Request, origin!);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                await context.Response.CompleteAsync();
            });

        application.MapGet(
            "/bootstrap-proof",
            async context =>
            {
                var origin = GetSingleHeader(context.Request.Headers, "Origin");
                if (!sessions.TryCreateBootstrapProof(
                        context.Request.Query["nonceId"],
                        origin,
                        context.Request.Query["challenge"],
                        out var proof))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                ApplyCorsHeaders(context.Response, context.Request, origin!);
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new { protocol = ProtocolVersion, proof });
            });

        application.MapGet(
            "/visual/{kind}/{id}",
            async context =>
            {
                var origin = GetSingleHeader(context.Request.Headers, "Origin");
                if (!RequestAuthentication.TryGetBearerToken(context.Request.Headers, out var token)
                    || !sessions.ValidateSession(token, origin))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var kind = (string?)context.Request.RouteValues["kind"];
                var id = (string?)context.Request.RouteValues["id"];
                if (kind is null || id is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var response = await visuals.LoadAsync(
                    kind,
                    Uri.UnescapeDataString(id),
                    context.RequestAborted);
                if (response is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                ApplyCorsHeaders(context.Response, context.Request, origin!);
                context.Response.Headers.CacheControl = "private, max-age=300";
                context.Response.Headers.ETag =
                    $"\"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(response.Bytes))}\"";
                context.Response.Headers["X-Wallpaper-Presentation"] = response.Presentation;
                if (!string.IsNullOrWhiteSpace(response.DisplayName))
                {
                    context.Response.Headers["X-Wallpaper-Display-Name"] =
                        Uri.EscapeDataString(response.DisplayName);
                }

                context.Response.ContentType = response.ContentType;
                context.Response.ContentLength = response.Bytes.Length;
                await context.Response.Body.WriteAsync(response.Bytes);
            });

        application.Map(
            "/ws",
            context => HandleWebSocketAsync(
                context,
                sessions,
                projection,
                fileCommands,
                port));
        return application;
    }

    private static async Task HandleWebSocketAsync(
        HttpContext context,
        SessionRegistry sessions,
        DesktopProjectionService projection,
        IFileCommandService fileCommands,
        int port)
    {
        var origin = GetSingleHeader(context.Request.Headers, "Origin");
        if (!context.WebSockets.IsWebSocketRequest || !sessions.HasAllowedOrigin(origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        ActiveSession? session = null;
        try
        {
            using var helloTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var helloBytes = await ReceiveMessageAsync(socket, helloTimeout.Token);
            if (helloBytes is null
                || !TryReadHello(helloBytes, out var nonce)
                || !sessions.TryAuthenticate(nonce!, origin!, out session))
            {
                await SendJsonAsync(socket, new { type = "error", code = "hello_rejected" });
                await socket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    "hello rejected",
                    CancellationToken.None);
                return;
            }

            var outgoing = Channel.CreateUnbounded<byte[]>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                });
            void OnSnapshotChanged(object? _, ProjectionSnapshot snapshot) =>
                outgoing.Writer.TryWrite(
                    SerializeJson(
                        new
                        {
                            type = "snapshot",
                            snapshot,
                            watch = projection.WatchStatus,
                        }));
            projection.SnapshotChanged += OnSnapshotChanged;
            try
            {
                await SendJsonAsync(
                    socket,
                    new
                    {
                        type = "helloAck",
                        protocol = ProtocolVersion,
                        sessionToken = session!.Token,
                        desktopRoot = projection.Current.RootPath,
                        httpBaseUrl = $"http://127.0.0.1:{port}",
                        snapshot = projection.Current,
                        watch = projection.WatchStatus,
                    });

                using var connection = new CancellationTokenSource();
                var receiveTask = ReceiveLoopAsync(
                    socket,
                    session.Token,
                    projection,
                    fileCommands,
                    outgoing.Writer,
                    connection.Token);
                var sendTask = SendOutgoingAsync(socket, outgoing.Reader, connection.Token);
                await Task.WhenAny(receiveTask, sendTask);
                connection.Cancel();
                await IgnoreConnectionEndAsync(receiveTask);
                await IgnoreConnectionEndAsync(sendTask);
            }
            finally
            {
                projection.SnapshotChanged -= OnSnapshotChanged;
                outgoing.Writer.TryComplete();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected for hello timeout, disconnect, or application shutdown.
        }
        catch (WebSocketException)
        {
            // Widget reload and Seelen restart close active sockets.
        }
        finally
        {
            if (session is not null)
            {
                sessions.RemoveSession(session.Token);
            }
        }
    }

    private static async Task ReceiveLoopAsync(
        WebSocket socket,
        string sessionToken,
        DesktopProjectionService projection,
        IFileCommandService fileCommands,
        ChannelWriter<byte[]> outgoing,
        CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(socket, cancellationToken);
            if (message is null)
            {
                return;
            }

            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!HasSession(root, sessionToken))
            {
                outgoing.TryWrite(SerializeJson(new { type = "error", code = "invalid_session" }));
                continue;
            }

            var type = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (type == "ping"
                && root.TryGetProperty("timestamp", out var timestamp)
                && timestamp.TryGetInt64(out var value))
            {
                outgoing.TryWrite(SerializeJson(new { type = "pong", timestamp = value }));
            }
            else if (type == "refresh")
            {
                await projection.RefreshAsync(cancellationToken: cancellationToken);
            }
            else if (type == "setFolderOrder"
                && root.TryGetProperty("orderedIds", out var ids)
                && ids.ValueKind == JsonValueKind.Array)
            {
                var order = ids.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .Take(4096)
                    .ToArray();
                var accepted = await projection.SetFolderOrderAsync(order, cancellationToken);
                outgoing.TryWrite(SerializeJson(new { type = "folderOrderAck", accepted }));
            }
            else if (type == "setRoot"
                && root.TryGetProperty("rootPath", out var rootPathElement)
                && rootPathElement.GetString() is { } rootPath)
            {
                var accepted = await projection.SetRootPathAsync(rootPath, cancellationToken);
                outgoing.TryWrite(
                    SerializeJson(
                        new
                        {
                            type = "setRootAck",
                            rootPath,
                            accepted,
                        }));
            }
            else if (type == "useDefaultRoot")
            {
                var accepted = await projection.UseDefaultRootAsync(cancellationToken);
                outgoing.TryWrite(
                    SerializeJson(
                        new
                        {
                            type = "useDefaultRootAck",
                            accepted,
                        }));
            }
            else if (type == "openFile"
                && root.TryGetProperty("fileId", out var fileIdElement)
                && fileIdElement.GetString() is { } fileId)
            {
                await OpenFileAsync(
                    fileId,
                    projection,
                    fileCommands,
                    outgoing,
                    cancellationToken);
            }
            else
            {
                outgoing.TryWrite(SerializeJson(new { type = "error", code = "invalid_message" }));
            }
        }
    }

    private static async Task OpenFileAsync(
        string fileId,
        DesktopProjectionService projection,
        IFileCommandService fileCommands,
        ChannelWriter<byte[]> outgoing,
        CancellationToken cancellationToken)
    {
        if (!projection.TryGetFile(fileId, out var target) || target is null)
        {
            outgoing.TryWrite(
                SerializeJson(
                    new
                    {
                        type = "openFileAck",
                        fileId,
                        accepted = false,
                        code = "target_missing",
                        message = "선택한 파일이 더 이상 현재 목록에 없습니다.",
                    }));
            return;
        }

        try
        {
            await fileCommands.OpenAsync(
                new FileCommandTarget(
                    target.RootPath,
                    target.File.RelativePath,
                    FileCommandItemKind.File),
                cancellationToken);
            outgoing.TryWrite(
                SerializeJson(
                    new
                    {
                        type = "openFileAck",
                        fileId,
                        accepted = true,
                        code = (string?)null,
                        message = (string?)null,
                    }));
        }
        catch (FileCommandException exception)
        {
            outgoing.TryWrite(
                SerializeJson(
                    new
                    {
                        type = "openFileAck",
                        fileId,
                        accepted = false,
                        code = exception.Error.ToString(),
                        message = exception.Message,
                    }));
        }
    }

    private static async Task SendOutgoingAsync(
        WebSocket socket,
        ChannelReader<byte[]> outgoing,
        CancellationToken cancellationToken)
    {
        await foreach (var message in outgoing.ReadAllAsync(cancellationToken))
        {
            await socket.SendAsync(
                message,
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }
    }

    private static bool HasSession(JsonElement root, string expected) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("sessionToken", out var token)
        && string.Equals(token.GetString(), expected, StringComparison.Ordinal);

    private static bool TryReadHello(ReadOnlySpan<byte> utf8, out string? nonce)
    {
        nonce = null;
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.GetString() != "hello"
                || !root.TryGetProperty("protocol", out var protocol)
                || protocol.GetInt32() != ProtocolVersion
                || !root.TryGetProperty("nonce", out var nonceElement))
            {
                return false;
            }

            nonce = nonceElement.GetString();
            return nonce is not null;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static async Task<byte[]?> ReceiveMessageAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>(4096);
        while (true)
        {
            var memory = writer.GetMemory(4096);
            var result = await socket.ReceiveAsync(memory, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text
                || writer.WrittenCount + result.Count > MaximumIncomingBytes)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.MessageTooBig,
                    "invalid message",
                    CancellationToken.None);
                return null;
            }

            writer.Advance(result.Count);
            if (result.EndOfMessage)
            {
                return writer.WrittenSpan.ToArray();
            }
        }
    }

    private static Task SendJsonAsync<T>(WebSocket socket, T message) =>
        socket.SendAsync(
            SerializeJson(message),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

    private static byte[] SerializeJson<T>(T message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);

    private static async Task IgnoreConnectionEndAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or WebSocketException or ChannelClosedException)
        {
            // Expected when either half of the socket finishes first.
        }
    }

    private static bool CanBindExclusively(int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true,
        };
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static string? GetSingleHeader(IHeaderDictionary headers, string name) =>
        headers.TryGetValue(name, out StringValues values) && values.Count == 1
            ? values[0]
            : null;

    private static void ApplyCorsHeaders(HttpResponse response, HttpRequest request, string origin)
    {
        response.Headers.AccessControlAllowOrigin = origin;
        response.Headers.Vary = "Origin";
        response.Headers.AccessControlAllowMethods = "GET, OPTIONS";
        response.Headers.AccessControlAllowHeaders = "Authorization";
        response.Headers.AccessControlExposeHeaders =
            "Content-Length, ETag, X-Wallpaper-Presentation, X-Wallpaper-Display-Name";
        if (string.Equals(
                request.Headers["Access-Control-Request-Private-Network"],
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }
    }
}
