using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Wallpaper.Seelen.M0.Companion;

public sealed class LoopbackServerTests
{
    [Fact]
    public async Task WebSocketHelloAndAuthenticatedBlobs_WorkEndToEnd()
    {
        Assert.True(
            BootstrapNonce.TryParse(Base64Url.CreateRandom256BitValue(), out var nonce));
        var sessions = new SessionRegistry();
        sessions.RegisterBootstrap(nonce!, OriginPolicy.SeelenAppOrigin);
        var startPort = ReserveAvailablePort();
        var options = new CompanionOptions(
            nonce!,
            OriginPolicy.SeelenAppOrigin,
            startPort,
            3);

        await using var server = await LoopbackServer.StartAsync(
            options,
            sessions,
            CancellationToken.None);
        using var http = new HttpClient();

        var challenge = RandomNumberGenerator.GetBytes(32);
        var proofRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{server.Port}/bootstrap-proof"
                + $"?nonceId={nonce!.Identifier}"
                + $"&challenge={Base64Url.Encode(challenge)}");
        proofRequest.Headers.Add("Origin", OriginPolicy.SeelenAppOrigin);
        using var proofResponse = await http.SendAsync(
            proofRequest,
            CancellationToken.None);
        var proofJson = JsonDocument.Parse(
            await proofResponse.Content.ReadAsByteArrayAsync(
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, proofResponse.StatusCode);
        Assert.Equal(
            nonce.CreateProof(challenge),
            proofJson.RootElement.GetProperty("proof").GetString());
        Assert.Equal(
            OriginPolicy.SeelenAppOrigin,
            proofResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Origin", OriginPolicy.SeelenAppOrigin);
        await webSocket.ConnectAsync(
            new Uri($"ws://127.0.0.1:{server.Port}/ws"),
            CancellationToken.None);
        await SendJsonAsync(
            webSocket,
            new
            {
                type = "hello",
                protocol = ProtocolMessages.Version,
                nonce = nonce.Encoded,
            });
        using var hello = JsonDocument.Parse(await ReceiveAsync(webSocket));
        var token = hello.RootElement.GetProperty("sessionToken").GetString();

        Assert.Equal("helloAck", hello.RootElement.GetProperty("type").GetString());
        Assert.NotNull(token);
        Assert.True(Base64Url.TryDecode256BitValue(token, out _));

        foreach (var kind in new[] { "icon", "image" })
        {
            var blobRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"http://127.0.0.1:{server.Port}/blob/{kind}");
            blobRequest.Headers.Add("Origin", OriginPolicy.SeelenAppOrigin);
            blobRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            using var blobResponse = await http.SendAsync(
                blobRequest,
                CancellationToken.None);
            var bytes = await blobResponse.Content.ReadAsByteArrayAsync(
                CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, blobResponse.StatusCode);
            Assert.Equal("image/png", blobResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(bytes.Length, blobResponse.Content.Headers.ContentLength);
            Assert.Equal(
                new byte[] { 0x89, 0x50, 0x4e, 0x47 },
                bytes[..4]);
        }

        var wrongHost = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{server.Port}/blob/icon");
        wrongHost.Headers.Host = $"localhost:{server.Port}";
        wrongHost.Headers.Add("Origin", OriginPolicy.SeelenAppOrigin);
        wrongHost.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        using var wrongHostResponse = await http.SendAsync(
            wrongHost,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, wrongHostResponse.StatusCode);
    }

    [Fact]
    public async Task StartAsync_SkipsAnOccupiedLoopbackPort()
    {
        using var collision = new TcpListener(IPAddress.Loopback, 0);
        collision.Start();
        var occupiedPort = ((IPEndPoint)collision.LocalEndpoint).Port;
        if (occupiedPort == IPEndPoint.MaxPort)
        {
            return;
        }

        Assert.True(
            BootstrapNonce.TryParse(Base64Url.CreateRandom256BitValue(), out var nonce));
        var sessions = new SessionRegistry();
        sessions.RegisterBootstrap(nonce!, OriginPolicy.SeelenAppOrigin);
        var options = new CompanionOptions(
            nonce!,
            OriginPolicy.SeelenAppOrigin,
            occupiedPort,
            2);

        await using var server = await LoopbackServer.StartAsync(
            options,
            sessions,
            CancellationToken.None);

        Assert.Equal(occupiedPort + 1, server.Port);
    }

    private static int ReserveAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Task SendJsonAsync(ClientWebSocket socket, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    private static async Task<byte[]> ReceiveAsync(ClientWebSocket socket)
    {
        var buffer = new byte[ProtocolMessages.MaximumMessageBytes];
        var result = await socket.ReceiveAsync(
            buffer,
            CancellationToken.None);
        Assert.True(result.EndOfMessage);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return buffer[..result.Count];
    }
}
