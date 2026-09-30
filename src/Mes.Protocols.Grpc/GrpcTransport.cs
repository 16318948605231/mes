using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Exceptions;
using Mes.Core.Transport;
using Mes.Protocols.Grpc.Generated;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Grpc;

/// <summary>
/// gRPC 传输：请求/响应走一元 <c>Invoke</c>，上报走一元 <c>Publish</c>，事件订阅走服务端流式 <c>Subscribe</c>。
/// 承载协议无关的字节负载，服务端只需实现内置的 <c>MesGateway</c> 契约。
/// </summary>
public sealed class GrpcTransport : MesTransportBase
{
    private readonly MesOptions _options;
    private readonly string _address;

    private GrpcChannel? _channel;
    private MesGateway.MesGatewayClient? _client;

    /// <summary>构造。</summary>
    public GrpcTransport(MesOptions options, ILogger? logger = null)
        : base(options?.Name ?? "Grpc", logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _address = ResolveAddress(_options);
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.Grpc;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities =>
        MesTransportCapabilities.Request | MesTransportCapabilities.Publish | MesTransportCapabilities.Subscribe;

    /// <inheritdoc />
    protected override Task DoConnectAsync(CancellationToken cancellationToken)
    {
        var channelOptions = new GrpcChannelOptions();
        if (_options.Tls.AllowUntrustedCertificates)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            channelOptions.HttpHandler = handler;
        }
        _channel = GrpcChannel.ForAddress(_address, channelOptions);
        _client = new MesGateway.MesGatewayClient(_channel);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        _client = null;
        if (_channel is not null)
        {
            try { await _channel.ShutdownAsync().ConfigureAwait(false); } catch { /* ignore */ }
            _channel.Dispose();
            _channel = null;
        }
    }

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new MesTransportException("gRPC 客户端未初始化。");
        var grpcRequest = new InvokeRequest
        {
            Channel = request.Channel,
            Verb = request.Verb ?? string.Empty,
            CorrelationId = request.CorrelationId,
            Body = ByteString.CopyFrom(request.Body ?? Array.Empty<byte>()),
            ContentType = request.ContentType ?? "application/json"
        };
        foreach (var header in request.Headers)
            grpcRequest.Headers[header.Key] = header.Value;

        var callOptions = BuildCallOptions(request.TimeoutMs ?? _options.TimeoutMs, cancellationToken);
        try
        {
            var reply = await client.InvokeAsync(grpcRequest, callOptions).ConfigureAwait(false);
            if (!reply.Success)
                return TransportResponse.Fail(string.IsNullOrEmpty(reply.ErrorMessage) ? "gRPC 调用失败。" : reply.ErrorMessage, reply.StatusCode);
            return TransportResponse.Ok(reply.Body.ToByteArray(),
                reply.StatusCode == 0 ? 200 : reply.StatusCode,
                string.IsNullOrEmpty(reply.ContentType) ? "application/json" : reply.ContentType);
        }
        catch (RpcException ex)
        {
            return TransportResponse.Fail($"gRPC 错误（{ex.StatusCode}）：{ex.Status.Detail}");
        }
    }

    /// <inheritdoc />
    protected override async Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new MesTransportException("gRPC 客户端未初始化。");
        var grpcRequest = new PublishRequest
        {
            Channel = message.Channel,
            CorrelationId = message.CorrelationId,
            Body = ByteString.CopyFrom(message.Body ?? Array.Empty<byte>()),
            ContentType = message.ContentType ?? "application/json"
        };
        var callOptions = BuildCallOptions(_options.TimeoutMs, cancellationToken);
        await client.PublishAsync(grpcRequest, callOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new MesTransportException("gRPC 客户端未初始化。");
        var cts = new CancellationTokenSource();
        var call = client.Subscribe(new SubscribeRequest { Channel = channel }, BuildCallOptions(0, cts.Token));
        var loop = Task.Run(() => ReadStreamAsync(call, handler, cts.Token), CancellationToken.None);
        IAsyncDisposable subscription = new Subscription(cts, call, loop);
        return Task.FromResult(subscription);
    }

    private async Task ReadStreamAsync(AsyncServerStreamingCall<Event> call, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        try
        {
            while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                var evt = call.ResponseStream.Current;
                var msg = new TransportMessage
                {
                    Channel = evt.Channel,
                    Body = evt.Body.ToByteArray(),
                    ContentType = string.IsNullOrEmpty(evt.ContentType) ? "application/json" : evt.ContentType,
                    CorrelationId = string.IsNullOrEmpty(evt.CorrelationId) ? Guid.NewGuid().ToString("N") : evt.CorrelationId
                };
                RaiseMessageReceived(msg);
                try
                {
                    await handler(msg, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RaiseError(ex, "Grpc.Handler");
                }
            }
        }
        catch (OperationCanceledException) { /* 正常关闭 */ }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { /* 正常关闭 */ }
        catch (Exception ex)
        {
            RaiseError(ex, "Grpc.Subscribe");
        }
    }

    private CallOptions BuildCallOptions(int timeoutMs, CancellationToken cancellationToken)
    {
        var metadata = new Metadata();
        switch (_options.Auth.Type)
        {
            case MesAuthType.BearerToken when !string.IsNullOrEmpty(_options.Auth.Token):
                metadata.Add("authorization", $"Bearer {_options.Auth.Token}");
                break;
            case MesAuthType.Basic or MesAuthType.UsernamePassword when _options.Auth.Username is not null:
                var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Auth.Username}:{_options.Auth.Password}"));
                metadata.Add("authorization", $"Basic {raw}");
                break;
            case MesAuthType.ApiKey when !string.IsNullOrEmpty(_options.Auth.ApiKey):
                metadata.Add(_options.Auth.ApiKeyHeader.ToLowerInvariant(), _options.Auth.ApiKey);
                break;
        }

        var options = new CallOptions(headers: metadata, cancellationToken: cancellationToken);
        if (timeoutMs > 0)
            options = options.WithDeadline(DateTime.UtcNow.AddMilliseconds(timeoutMs));
        return options;
    }

    private static string ResolveAddress(MesOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Endpoint.BaseAddress))
            return options.Endpoint.BaseAddress!;
        var scheme = options.Endpoint.UseTls || options.Tls.Enabled ? "https" : "http";
        var host = options.Endpoint.Host ?? "localhost";
        var port = options.Endpoint.Port ?? (scheme == "https" ? 443 : 80);
        return $"{scheme}://{host}:{port}";
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly AsyncServerStreamingCall<Event> _call;
        private readonly Task _loop;
        public Subscription(CancellationTokenSource cts, AsyncServerStreamingCall<Event> call, Task loop)
        {
            _cts = cts; _call = call; _loop = loop;
        }
        public async ValueTask DisposeAsync()
        {
            try { _cts.Cancel(); } catch { /* ignore */ }
            try { await _loop.ConfigureAwait(false); } catch { /* ignore */ }
            _call.Dispose();
            _cts.Dispose();
        }
    }
}
