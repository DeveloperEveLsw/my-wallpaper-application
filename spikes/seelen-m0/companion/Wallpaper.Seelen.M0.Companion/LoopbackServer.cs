using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Primitives;

namespace Wallpaper.Seelen.M0.Companion;

internal sealed class LoopbackServer : IAsyncDisposable
{
    private static readonly byte[] IconPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nksAAAAASUVORK5CYII=");
    private static readonly byte[] ImagePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAQAAABFaP0WAAAADElEQVR42mNk+M8AAAICAQB7CY6gAAAAAElFTkSuQmCC");

    private LoopbackServer(WebApplication application, int port)
    {
        Application = application;
        Port = port;
    }

    public WebApplication Application { get; }

    public int Port { get; }

    public static async Task<LoopbackServer> StartAsync(
        CompanionOptions options,
        SessionRegistry sessions,
        CancellationToken cancellationToken)
    {
        foreach (var port in options.CandidatePorts)
        {
            if (!CanBindExclusively(port))
            {
                continue;
            }

            var application = BuildApplication(port, sessions);
            try
            {
                await application.StartAsync(cancellationToken);
                return new LoopbackServer(application, port);
            }
            catch
            {
                await application.DisposeAsync();
                throw;
            }
        }

        throw new IOException("No loopback port in the M0 allowlist could be bound.");
    }

    public async ValueTask DisposeAsync()
    {
        await Application.DisposeAsync();
    }

    private static bool CanBindExclusively(int port)
    {
        using var socket = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp)
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

    private static WebApplication BuildApplication(
        int port,
        SessionRegistry sessions)
    {
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(LoopbackServer).Assembly.FullName,
                ContentRootPath = Path.GetTempPath(),
                EnvironmentName = Environments.Production,
            });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
            });
        });

        var application = builder.Build();
        var hostPolicy = new HostPolicy(port);

        application.Use(
            async (context, next) =>
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

        application.UseWebSockets(
            new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(15),
            });

        application.MapMethods(
            "/blob/{kind}",
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

        application.MapMethods(
            "/bootstrap-proof",
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
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        protocol = ProtocolMessages.Version,
                        proof,
                    });
            });

        application.MapGet(
            "/blob/{kind}",
            async context =>
            {
                var origin = GetSingleHeader(context.Request.Headers, "Origin");
                if (!RequestAuthentication.TryGetBearerToken(
                        context.Request.Headers,
                        out var token)
                    || !sessions.ValidateSession(token, origin))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var kind = (string?)context.Request.RouteValues["kind"];
                var bytes = kind switch
                {
                    "icon" => IconPng,
                    "image" => ImagePng,
                    _ => null,
                };

                if (bytes is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                ApplyCorsHeaders(context.Response, context.Request, origin!);
                context.Response.Headers.CacheControl = "no-store";
                context.Response.ContentType = "image/png";
                context.Response.ContentLength = bytes.Length;
                await context.Response.Body.WriteAsync(bytes);
            });

        application.Map(
            "/ws",
            context => HandleWebSocketAsync(context, sessions, port));

        return application;
    }

    private static async Task HandleWebSocketAsync(
        HttpContext context,
        SessionRegistry sessions,
        int port)
    {
        var origin = GetSingleHeader(context.Request.Headers, "Origin");
        if (!context.WebSockets.IsWebSocketRequest
            || !sessions.HasAllowedOrigin(origin))
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
                await SendJsonAsync(
                    socket,
                    new ErrorMessage("error", "hello_rejected"),
                    CancellationToken.None);
                await socket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    "hello rejected",
                    CancellationToken.None);
                return;
            }

            var acknowledgement = new HelloAcknowledgement(
                "helloAck",
                ProtocolMessages.Version,
                session!.Token,
                DesktopRootResolver.Resolve(),
                $"http://127.0.0.1:{port}");
            await SendJsonAsync(socket, acknowledgement, CancellationToken.None);

            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveMessageAsync(socket, CancellationToken.None);
                if (message is null)
                {
                    break;
                }

                if (TryReadPing(message, session.Token, out var timestamp))
                {
                    await SendJsonAsync(
                        socket,
                        new PongMessage("pong", timestamp),
                        CancellationToken.None);
                }
                else
                {
                    await SendJsonAsync(
                        socket,
                        new ErrorMessage("error", "invalid_message"),
                        CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    "hello timeout",
                    CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // A disconnected widget is expected during reload or Seelen restart.
        }
        finally
        {
            if (session is not null)
            {
                sessions.RemoveSession(session.Token);
            }
        }
    }

    private static async Task<byte[]?> ReceiveMessageAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>(ProtocolMessages.MaximumMessageBytes);
        while (true)
        {
            var memory = writer.GetMemory(1024);
            var result = await socket.ReceiveAsync(memory, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text
                || writer.WrittenCount + result.Count > ProtocolMessages.MaximumMessageBytes)
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

    private static bool TryReadHello(
        ReadOnlySpan<byte> utf8,
        out string? encodedNonce)
    {
        encodedNonce = null;
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.GetString() != "hello"
                || !root.TryGetProperty("protocol", out var protocol)
                || protocol.GetInt32() != ProtocolMessages.Version
                || !root.TryGetProperty("nonce", out var nonce))
            {
                return false;
            }

            encodedNonce = nonce.GetString();
            return encodedNonce is not null;
        }
        catch (Exception exception) when (
            exception is JsonException
                or InvalidOperationException
                or FormatException)
        {
            return false;
        }
    }

    private static bool TryReadPing(
        ReadOnlySpan<byte> utf8,
        string expectedToken,
        out long timestamp)
    {
        timestamp = 0;
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("type", out var type)
                && type.GetString() == "ping"
                && root.TryGetProperty("sessionToken", out var token)
                && token.GetString() == expectedToken
                && root.TryGetProperty("timestamp", out var time)
                && time.TryGetInt64(out timestamp);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Task SendJsonAsync<T>(
        WebSocket socket,
        T message,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            message,
            JsonSerializerOptions.Web);
        return socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    private static string? GetSingleHeader(
        IHeaderDictionary headers,
        string name)
    {
        return headers.TryGetValue(name, out StringValues values)
            && values.Count == 1
            ? values[0]
            : null;
    }

    private static void ApplyCorsHeaders(
        HttpResponse response,
        HttpRequest request,
        string origin)
    {
        response.Headers.AccessControlAllowOrigin = origin;
        response.Headers.Vary = "Origin";
        response.Headers.AccessControlAllowMethods = "GET, OPTIONS";
        response.Headers.AccessControlAllowHeaders = "Authorization";

        if (string.Equals(
                request.Headers["Access-Control-Request-Private-Network"],
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }
    }
}
