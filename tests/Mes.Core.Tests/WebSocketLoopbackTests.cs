using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Core.Builder;
using Mes.Core.Models;
using Mes.Protocols.WebSocket;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// WebSocket Provider 端到端测试：用本地 <see cref="TcpListener"/> + 手工握手 +
/// <c>WebSocket.CreateFromStream</c> 搭建回环服务，验证请求/响应与订阅推送。
/// （HttpListener.AcceptWebSocketAsync 在 Linux 上不受支持，故手工实现握手。）
/// </summary>
public sealed class WebSocketLoopbackTests
{
    [Fact]
    public async Task Request_And_Subscribe_OverWebSocket()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serverTask = Task.Run(() => RunServerAsync(listener, serverCts.Token));

        await using var client = MesClientBuilder.Create()
            .UseWebSocket($"ws://localhost:{port}/ws")
            .Build();

        // 1) 请求/响应
        var wo = await client.GetWorkOrderAsync("WO-1");
        Assert.True(wo.Success, wo.Message);
        Assert.Equal("WO-1", wo.Value!.Id);

        // 2) 订阅推送
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sub = await client.SubscribeAsync<Alarm>("mes/alarm", (a, _) =>
        {
            received.TrySetResult(a.Code);
            return Task.CompletedTask;
        });

        var code = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("E-777", code);

        serverCts.Cancel();
        listener.Stop();
    }

    private static async Task RunServerAsync(TcpListener listener, CancellationToken ct)
    {
        using var tcp = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        var stream = tcp.GetStream();

        // --- 手工 WebSocket 握手 ---
        var requestText = await ReadHttpHeadersAsync(stream, ct).ConfigureAwait(false);
        var key = ParseHeader(requestText, "Sec-WebSocket-Key");
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var handshake =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
        var hb = Encoding.ASCII.GetBytes(handshake);
        await stream.WriteAsync(hb, ct).ConfigureAwait(false);

        using var ws = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));

        var buffer = new byte[16 * 1024];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                break;
            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "request")
            {
                var corr = root.GetProperty("correlationId").GetString();
                var reply = JsonSerializer.Serialize(new
                {
                    type = "response",
                    correlationId = corr,
                    success = true,
                    body = "{\"id\":\"WO-1\",\"productCode\":\"P1\",\"quantity\":10}"
                });
                await SendTextAsync(ws, reply, ct).ConfigureAwait(false);
            }
            else if (type == "subscribe")
            {
                var channel = root.GetProperty("channel").GetString();
                var evt = JsonSerializer.Serialize(new
                {
                    type = "event",
                    channel,
                    body = "{\"code\":\"E-777\"}"
                });
                await SendTextAsync(ws, evt, ct).ConfigureAwait(false);
            }
        }
    }

    private static Task SendTextAsync(WebSocket ws, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static async Task<string> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (!(sb.Length >= 4 && sb[sb.Length - 4] == '\r' && sb[sb.Length - 3] == '\n'
                 && sb[sb.Length - 2] == '\r' && sb[sb.Length - 1] == '\n'))
        {
            var n = await stream.ReadAsync(buf, ct).ConfigureAwait(false);
            if (n == 0) break;
            sb.Append((char)buf[0]);
        }
        return sb.ToString();
    }

    private static string ParseHeader(string request, string name)
    {
        foreach (var line in request.Split("\r\n"))
        {
            var idx = line.IndexOf(':');
            if (idx > 0 && line.Substring(0, idx).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return line.Substring(idx + 1).Trim();
        }
        return string.Empty;
    }
}
