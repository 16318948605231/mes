using System.Buffers;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace Mes.Protocols.Mqtt;

/// <summary>
/// MQTT 传输：支持发布/订阅语义（QoS 0/1/2、保留标志、TLS、用户名口令认证）。
/// 适用于设备状态/检测结果/报警等遥测上报与服务器推送场景。
/// </summary>
public sealed class MqttTransport : MesTransportBase
{
    private readonly MesOptions _options;
    private readonly IMqttClient _client;
    private readonly List<Subscription> _subscriptions = new();
    private readonly object _sync = new();

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
    public override MesTransportCapabilities Capabilities => MesTransportCapabilities.Publish | MesTransportCapabilities.Subscribe;

    /// <inheritdoc />
    protected override async Task DoConnectAsync(CancellationToken cancellationToken)
    {
        var host = _options.Endpoint.Host ?? throw new Mes.Core.Exceptions.MesConfigurationException("MQTT 需要设置 Endpoint.Host。");
        var useTls = _options.Endpoint.UseTls || _options.Tls.Enabled;
        var port = _options.Endpoint.Port ?? (useTls ? 8883 : 1883);
        var clientId = _options.GetProperty("ClientId") ?? $"{_options.Name}-{Guid.NewGuid():N}";

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
        await base.DisposeAsync().ConfigureAwait(false);
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
