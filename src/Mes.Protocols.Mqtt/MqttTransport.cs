using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Exceptions;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace Mes.Protocols.Mqtt;

/// <summary>
/// MQTT 传输：支持发布/订阅语义（QoS 0/1/2、保留标志、TLS、用户名口令认证），
/// 并基于 MQTT 5 的“响应主题 + 关联数据”实现请求/响应（RPC）以承载查询类操作。
/// 适用于设备状态/检测结果/报警等遥测上报、服务器推送以及工单/配方等查询场景。
/// </summary>
public sealed class MqttTransport : MesTransportBase
{
    private readonly MesOptions _options;
    private readonly IMqttClient _client;
    private readonly List<Subscription> _subscriptions = new();
    private readonly object _sync = new();

    // 请求/响应（RPC）：关联标识 -> 等待中的响应。
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TransportMessage>> _pendingRequests = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _responseSubscriptionGate = new(1, 1);
    private string _responseTopicRoot = string.Empty;
    private bool _responseSubscribed;

    /// <summary>构造。</summary>
    public MqttTransport(MesOptions options, ILogger? logger = null)
        : base(options?.Name ?? "Mqtt", logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.Mqtt;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities
        => MesTransportCapabilities.Request | MesTransportCapabilities.Publish | MesTransportCapabilities.Subscribe;

    /// <inheritdoc />
    protected override async Task DoConnectAsync(CancellationToken cancellationToken)
    {
        var host = _options.Endpoint.Host ?? throw new MesConfigurationException("MQTT 需要设置 Endpoint.Host。");
        var useTls = _options.Endpoint.UseTls || _options.Tls.Enabled;
        var port = _options.Endpoint.Port ?? (useTls ? 8883 : 1883);
        var clientId = _options.GetProperty("ClientId") ?? $"{_options.Name}-{Guid.NewGuid():N}";

        var prefix = _options.GetProperty("TopicPrefix") ?? "mes";
        _responseTopicRoot = $"{prefix}/rpc/response/{clientId}";
        // 断线重连后需重新订阅响应主题。
        Volatile.Write(ref _responseSubscribed, false);

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId(clientId)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrEmpty(_options.Auth.Username))
            builder = builder.WithCredentials(_options.Auth.Username, _options.Auth.Password ?? string.Empty);

        if (useTls)
        {
            builder = builder.WithTlsOptions(o =>
            {
                o.UseTls();
                if (_options.Tls.AllowUntrustedCertificates)
                {
                    o.WithAllowUntrustedCertificates(true);
                    o.WithIgnoreCertificateChainErrors(true);
                    o.WithIgnoreCertificateRevocationErrors(true);
                    o.WithCertificateValidationHandler(_ => true);
                }
            });
        }

        await _client.ConnectAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
            await _client.DisconnectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        var mqttMessage = new MqttApplicationMessageBuilder()
            .WithTopic(message.Channel)
            .WithPayload(message.Body ?? Array.Empty<byte>())
            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)Math.Clamp(message.Qos, 0, 2))
            .WithRetainFlag(message.Retain)
            .Build();
        if (!string.IsNullOrEmpty(message.ContentType))
            mqttMessage.ContentType = message.ContentType;

        await _client.PublishAsync(mqttMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 基于 MQTT 5 “响应主题 + 关联数据”实现请求/响应：把请求发布到操作主题（通道），
    /// 并在专属响应主题上等待携带相同关联数据的回复。响应端（MES/网关）读取
    /// <c>ResponseTopic</c> 与 <c>CorrelationData</c>，将结果发布回该响应主题即可。
    /// </summary>
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        await EnsureResponseSubscriptionAsync(cancellationToken).ConfigureAwait(false);

        var correlationId = request.CorrelationId;
        var responseTopic = $"{_responseTopicRoot}/{correlationId}";
        var tcs = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(correlationId, tcs))
            throw new MesTransportException($"MQTT 请求关联标识重复：{correlationId}。");

        try
        {
            var builder = new MqttApplicationMessageBuilder()
                .WithTopic(request.Channel)
                .WithPayload(request.Body ?? Array.Empty<byte>())
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithResponseTopic(responseTopic)
                .WithCorrelationData(Encoding.UTF8.GetBytes(correlationId));
            if (!string.IsNullOrEmpty(request.ContentType))
                builder = builder.WithContentType(request.ContentType);

            await _client.PublishAsync(builder.Build(), cancellationToken).ConfigureAwait(false);

            var timeoutMs = request.TimeoutMs ?? _options.TimeoutMs;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutMs > 0)
                timeoutCts.CancelAfter(timeoutMs);

            using var registration = timeoutCts.Token.Register(
                static state => ((TaskCompletionSource<TransportMessage>)state!).TrySetCanceled(),
                tcs);

            TransportMessage reply;
            try
            {
                reply = await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return TransportResponse.Fail($"MQTT 请求在 {timeoutMs}ms 内未收到响应（主题 {request.Channel}）。", 504);
            }

            return TransportResponse.Ok(reply.Body, 200, reply.ContentType ?? "application/json");
        }
        finally
        {
            _pendingRequests.TryRemove(correlationId, out _);
        }
    }

    /// <summary>确保已订阅本客户端的响应主题（首次请求时惰性建立，断线后重建）。</summary>
    private async Task EnsureResponseSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _responseSubscribed))
            return;

        await _responseSubscriptionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_responseSubscribed)
                return;

            var options = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic($"{_responseTopicRoot}/#").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                .Build();
            await _client.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _responseSubscribed, true);
        }
        finally
        {
            _responseSubscriptionGate.Release();
        }
    }

    /// <inheritdoc />
    protected override async Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        var subscription = new Subscription(channel, handler);
        lock (_sync)
            _subscriptions.Add(subscription);

        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(channel).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();
        await _client.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);

        return new SubscriptionHandle(this, subscription);
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var payload = e.ApplicationMessage.Payload.IsEmpty
            ? Array.Empty<byte>()
            : e.ApplicationMessage.Payload.ToArray();

        var message = new TransportMessage
        {
            Channel = e.ApplicationMessage.Topic,
            Body = payload,
            ContentType = e.ApplicationMessage.ContentType,
            Qos = (int)e.ApplicationMessage.QualityOfServiceLevel,
            Retain = e.ApplicationMessage.Retain
        };

        // 请求/响应：命中等待中的关联标识则作为 RPC 回复处理，不再派发给普通订阅者。
        var correlationData = e.ApplicationMessage.CorrelationData;
        if (correlationData is { Length: > 0 })
        {
            var correlationId = Encoding.UTF8.GetString(correlationData);
            if (_pendingRequests.TryGetValue(correlationId, out var pending))
            {
                pending.TrySetResult(message);
                return;
            }
        }

        RaiseMessageReceived(message);

        Subscription[] matches;
        lock (_sync)
            matches = _subscriptions.Where(s => TopicMatches(s.Filter, message.Channel)).ToArray();

        foreach (var sub in matches)
        {
            try
            {
                await sub.Handler(message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaiseError(ex, "MqttSubscriptionHandler");
            }
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        Volatile.Write(ref _responseSubscribed, false);

        // 连接断开时立即失败所有等待中的请求，避免调用方一直等到超时；
        // 清空字典以防重连后关联标识复用导致 TryAdd 冲突或条目泄漏。
        foreach (var pending in _pendingRequests.Values)
            pending.TrySetException(new MesTransportException($"MQTT 连接已断开（{e.Reason}），请求未完成。"));
        _pendingRequests.Clear();

        SetState(MesConnectionState.Disconnected, e.Reason.ToString());
        return Task.CompletedTask;
    }

    private void RemoveSubscription(Subscription subscription)
    {
        lock (_sync)
            _subscriptions.Remove(subscription);
    }

    /// <summary>MQTT 主题匹配（支持单层 + 与多层 # 通配符）。</summary>
    internal static bool TopicMatches(string filter, string topic)
    {
        if (string.Equals(filter, topic, StringComparison.Ordinal))
            return true;

        var f = filter.Split('/');
        var t = topic.Split('/');
        for (var i = 0; i < f.Length; i++)
        {
            if (f[i] == "#")
                return true;
            if (i >= t.Length)
                return false;
            if (f[i] == "+")
                continue;
            if (!string.Equals(f[i], t[i], StringComparison.Ordinal))
                return false;
        }
        return f.Length == t.Length;
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        _client.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
        _client.DisconnectedAsync -= OnDisconnectedAsync;

        foreach (var pending in _pendingRequests.Values)
            pending.TrySetException(new MesTransportException("MQTT 传输已释放，请求未完成。"));
        _pendingRequests.Clear();

        await base.DisposeAsync().ConfigureAwait(false);
        _responseSubscriptionGate.Dispose();
        _client.Dispose();
    }

    private sealed record Subscription(string Filter, Func<TransportMessage, CancellationToken, Task> Handler);

    private sealed class SubscriptionHandle : IAsyncDisposable
    {
        private readonly MqttTransport _transport;
        private readonly Subscription _subscription;

        public SubscriptionHandle(MqttTransport transport, Subscription subscription)
        {
            _transport = transport;
            _subscription = subscription;
        }

        public async ValueTask DisposeAsync()
        {
            _transport.RemoveSubscription(_subscription);
            try
            {
                if (_transport._client.IsConnected)
                    await _transport._client.UnsubscribeAsync(
                        new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(_subscription.Filter).Build())
                        .ConfigureAwait(false);
            }
            catch
            {
                // 取消订阅失败可忽略
            }
        }
    }
}
