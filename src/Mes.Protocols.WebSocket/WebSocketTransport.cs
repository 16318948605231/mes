using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Exceptions;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.WebSocket;

/// <summary>
/// WebSocket 传输：基于单条长连接，用统一的 JSON 信封承载请求/响应与发布/订阅。
/// 信封字段：<c>type</c>（request/response/publish/event/subscribe/unsubscribe）、<c>channel</c>、
/// <c>correlationId</c>、<c>success</c>、<c>error</c>、<c>body</c>（内层 JSON 文本）。
/// 该约定同时兼容自定义 WebSocket 网关与 SignalR 风格的消息路由。
/// </summary>
public sealed class WebSocketTransport : MesTransportBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MesOptions _options;
    private readonly Func<ClientWebSocket>? _socketFactory;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WsEnvelope>> _pending = new();
    private readonly ConcurrentDictionary<string, Func<TransportMessage, CancellationToken, Task>> _subscriptions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;

    /// <summary>构造。<paramref name="socketFactory"/> 便于测试注入自定义客户端。</summary>
    public WebSocketTransport(MesOptions options, Func<ClientWebSocket>? socketFactory = null, ILogger? logger = null)
        : base(options?.Name ?? "WebSocket", logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _socketFactory = socketFactory;
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.WebSocket;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities => MesTransportCapabilities.All;

    /// <inheritdoc />
    protected override async Task DoConnectAsync(CancellationToken cancellationToken)
    {
        var address = _options.Endpoint.BaseAddress
            ?? throw new Mes.Core.Exceptions.MesConfigurationException("WebSocket 传输需要设置 Endpoint.BaseAddress（ws:// 或 wss:// URL）。");

        var ws = _socketFactory?.Invoke() ?? new ClientWebSocket();
        ApplyAuth(ws);
        var subProtocol = _options.GetProperty("SubProtocol");
        if (!string.IsNullOrWhiteSpace(subProtocol))
            ws.Options.AddSubProtocol(subProtocol);

        await ws.ConnectAsync(new Uri(address, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
        _ws = ws;

        _receiveCts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
    }

    /// <inheritdoc />
    protected override async Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        try { _receiveCts?.Cancel(); } catch { /* ignore */ }

        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", cancellationToken).ConfigureAwait(false);
            }
            catch { /* ignore */ }
        }

        FailPending(new MesTransportException("WebSocket 连接已关闭。"));
        _ws?.Dispose();
        _ws = null;
    }

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var envelope = new WsEnvelope
        {
            Type = "request",
            Channel = request.Channel,
            CorrelationId = request.CorrelationId,
            Body = request.Body is { Length: > 0 } ? Encoding.UTF8.GetString(request.Body) : null
        };

        var tcs = new TaskCompletionSource<WsEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.CorrelationId] = tcs;
        try
        {
            await SendEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);

            var timeout = request.TimeoutMs ?? _options.TimeoutMs;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout > 0) timeoutCts.CancelAfter(timeout);

            using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
            {
                var reply = await tcs.Task.ConfigureAwait(false);
                return new TransportResponse
                {
                    Success = reply.Success ?? true,
                    StatusCode = (reply.Success ?? true) ? 200 : 500,
                    Body = reply.Body is null ? null : Encoding.UTF8.GetBytes(reply.Body),
                    ContentType = "application/json",
                    CorrelationId = request.CorrelationId,
                    ErrorMessage = reply.Error
                };
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TransportResponse.Fail($"WebSocket 请求超时（correlationId={request.CorrelationId}）。");
        }
        finally
        {
            _pending.TryRemove(request.CorrelationId, out _);
        }
    }

    /// <inheritdoc />
    protected override Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        var envelope = new WsEnvelope
        {
            Type = "publish",
            Channel = message.Channel,
            CorrelationId = message.CorrelationId,
            Body = message.Body is { Length: > 0 } ? Encoding.UTF8.GetString(message.Body) : null
        };
        return SendEnvelopeAsync(envelope, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        _subscriptions[channel] = handler;
        await SendEnvelopeAsync(new WsEnvelope { Type = "subscribe", Channel = channel }, cancellationToken).ConfigureAwait(false);
        return new Subscription(this, channel);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var builder = new ArraySegmentBuilder();
        try
        {
            while (!cancellationToken.IsCancellationRequested && _ws is { State: WebSocketState.Open })
            {
                WebSocketReceiveResult result;
                builder.Reset();
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        SetState(MesConnectionState.Disconnected, "服务端关闭连接");
                        FailPending(new MesTransportException("WebSocket 服务端关闭连接。"));
                        return;
                    }
                    builder.Append(buffer, result.Count);
                }
                while (!result.EndOfMessage);

                var text = Encoding.UTF8.GetString(builder.ToArray());
                await DispatchAsync(text, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* 正常关闭 */ }
        catch (Exception ex)
        {
            RaiseError(ex, "WebSocket.ReceiveLoop");
            FailPending(ex);
        }
    }

    private async Task DispatchAsync(string text, CancellationToken cancellationToken)
    {
        WsEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<WsEnvelope>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }
        if (envelope is null)
            return;

        switch (envelope.Type)
        {
            case "response":
                if (envelope.CorrelationId is not null && _pending.TryGetValue(envelope.CorrelationId, out var tcs))
                    tcs.TrySetResult(envelope);
                break;

            case "event":
            case "publish":
                if (envelope.Channel is not null && _subscriptions.TryGetValue(envelope.Channel, out var handler))
                {
                    var msg = new TransportMessage
                    {
                        Channel = envelope.Channel,
                        Body = envelope.Body is null ? null : Encoding.UTF8.GetBytes(envelope.Body),
                        ContentType = "application/json",
                        CorrelationId = envelope.CorrelationId ?? Guid.NewGuid().ToString("N")
                    };
                    RaiseMessageReceived(msg);
                    await handler(msg, cancellationToken).ConfigureAwait(false);
                }
                break;
        }
    }

    private async Task SendEnvelopeAsync(WsEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_ws is not { State: WebSocketState.Open })
            throw new MesTransportException("WebSocket 未连接。");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void FailPending(Exception ex)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetException(ex);
        }
    }

    private void ApplyAuth(ClientWebSocket ws)
    {
        var auth = _options.Auth;
        switch (auth.Type)
        {
            case MesAuthType.Basic:
            case MesAuthType.UsernamePassword:
                var raw = Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Password}");
                ws.Options.SetRequestHeader("Authorization", "Basic " + Convert.ToBase64String(raw));
                break;
            case MesAuthType.BearerToken:
                if (!string.IsNullOrEmpty(auth.Token))
                    ws.Options.SetRequestHeader("Authorization", "Bearer " + auth.Token);
                break;
            case MesAuthType.ApiKey:
                if (!string.IsNullOrEmpty(auth.ApiKey))
                    ws.Options.SetRequestHeader(auth.ApiKeyHeader, auth.ApiKey);
                break;
        }
    }

    private void Unsubscribe(string channel)
    {
        _subscriptions.TryRemove(channel, out _);
        if (_ws is { State: WebSocketState.Open })
        {
            _ = SendEnvelopeAsync(new WsEnvelope { Type = "unsubscribe", Channel = channel }, CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        try { _receiveCts?.Cancel(); } catch { /* ignore */ }
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.ConfigureAwait(false); } catch { /* ignore */ }
        }
        _receiveCts?.Dispose();
        _sendGate.Dispose();
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private readonly WebSocketTransport _owner;
        private readonly string _channel;
        public Subscription(WebSocketTransport owner, string channel) { _owner = owner; _channel = channel; }
        public ValueTask DisposeAsync()
        {
            _owner.Unsubscribe(_channel);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WsEnvelope
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("channel")] public string? Channel { get; set; }
        [JsonPropertyName("correlationId")] public string? CorrelationId { get; set; }
        [JsonPropertyName("success")] public bool? Success { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
    }

    private sealed class ArraySegmentBuilder
    {
        private byte[] _buffer = new byte[16 * 1024];
        private int _length;
        public void Reset() => _length = 0;
        public void Append(byte[] source, int count)
        {
            if (_length + count > _buffer.Length)
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
            Array.Copy(source, 0, _buffer, _length, count);
            _length += count;
        }
        public byte[] ToArray()
        {
            var result = new byte[_length];
            Array.Copy(_buffer, result, _length);
            return result;
        }
    }
}
