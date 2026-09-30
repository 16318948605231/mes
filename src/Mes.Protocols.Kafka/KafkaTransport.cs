using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Exceptions;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Kafka;

/// <summary>
/// Apache Kafka 传输：上报类走发布语义；查询类基于“回复主题 + 关联标识”实现请求/响应（RPC）。
/// 生产者与消费者复用 <c>librdkafka</c>；订阅每个通道对应一个后台消费循环。
/// </summary>
public sealed class KafkaTransport : MesTransportBase
{
    private const string HeaderCorrelationId = "mes-correlation-id";
    private const string HeaderReplyTopic = "mes-reply-topic";

    private readonly MesOptions _options;
    private readonly string _bootstrapServers;
    private readonly string _clientId;
    private readonly string _replyTopic;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]?>> _pending = new();

    private IProducer<string, byte[]>? _producer;
    private CancellationTokenSource? _replyCts;
    private Task? _replyLoop;

    /// <summary>构造。</summary>
    public KafkaTransport(MesOptions options, ILogger? logger = null)
        : base(options?.Name ?? "Kafka", logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _bootstrapServers = _options.GetProperty("BootstrapServers")
            ?? (_options.Endpoint.Host is { } h ? $"{h}:{_options.Endpoint.Port ?? 9092}" : null)
            ?? throw new MesConfigurationException("Kafka 传输需要设置 BootstrapServers 或 Endpoint.Host。");
        _clientId = _options.GetProperty("ClientId") ?? $"{_options.Name}-{Guid.NewGuid():N}";
        var prefix = _options.GetProperty("TopicPrefix") ?? "mes";
        _replyTopic = _options.GetProperty("ReplyTopic") ?? $"{prefix}.reply.{_clientId}";
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.Kafka;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities => MesTransportCapabilities.All;

    /// <inheritdoc />
    protected override Task DoConnectAsync(CancellationToken cancellationToken)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = _bootstrapServers,
            ClientId = _clientId,
            Acks = Acks.All,
            MessageTimeoutMs = _options.TimeoutMs > 0 ? _options.TimeoutMs : 30_000
        };
        ApplySecurity(config);
        _producer = new ProducerBuilder<string, byte[]>(config).Build();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        try { _replyCts?.Cancel(); } catch { /* ignore */ }
        if (_replyLoop is not null)
        {
            try { await _replyLoop.ConfigureAwait(false); } catch { /* ignore */ }
            _replyLoop = null;
        }
        FailPending(new MesTransportException("Kafka 连接已关闭。"));

        _producer?.Flush(TimeSpan.FromSeconds(5));
        _producer?.Dispose();
        _producer = null;
    }

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        EnsureReplyLoop();
        var producer = _producer ?? throw new MesTransportException("Kafka 生产者未初始化。");

        var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.CorrelationId] = tcs;
        try
        {
            var headers = new Headers
            {
                { HeaderCorrelationId, Encoding.UTF8.GetBytes(request.CorrelationId) },
                { HeaderReplyTopic, Encoding.UTF8.GetBytes(_replyTopic) }
            };
            await producer.ProduceAsync(request.Channel, new Message<string, byte[]>
            {
                Key = request.CorrelationId,
                Value = request.Body ?? Array.Empty<byte>(),
                Headers = headers
            }, cancellationToken).ConfigureAwait(false);

            var timeout = request.TimeoutMs ?? _options.TimeoutMs;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout > 0) timeoutCts.CancelAfter(timeout);
            using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
            {
                var body = await tcs.Task.ConfigureAwait(false);
                return TransportResponse.Ok(body, 200, "application/json");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TransportResponse.Fail($"Kafka 请求超时（correlationId={request.CorrelationId}）。");
        }
        finally
        {
            _pending.TryRemove(request.CorrelationId, out _);
        }
    }

    /// <inheritdoc />
    protected override async Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        var producer = _producer ?? throw new MesTransportException("Kafka 生产者未初始化。");
        var headers = new Headers { { HeaderCorrelationId, Encoding.UTF8.GetBytes(message.CorrelationId) } };
        await producer.ProduceAsync(message.Channel, new Message<string, byte[]>
        {
            Key = message.CorrelationId,
            Value = message.Body ?? Array.Empty<byte>(),
            Headers = headers
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        var groupId = _options.GetProperty("GroupId") ?? $"{_clientId}-sub";
        var cts = new CancellationTokenSource();
        var loop = Task.Run(() => ConsumeLoopAsync(channel, groupId, handler, cts.Token), CancellationToken.None);
        IAsyncDisposable subscription = new Subscription(cts, loop);
        return Task.FromResult(subscription);
    }

    private void EnsureReplyLoop()
    {
        if (_replyLoop is not null)
            return;
        _replyCts = new CancellationTokenSource();
        _replyLoop = Task.Run(() => RunReplyLoop(_replyCts.Token), CancellationToken.None);
    }

    private void RunReplyLoop(CancellationToken cancellationToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = $"{_clientId}-reply",
            ClientId = $"{_clientId}-reply",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true
        };
        ApplySecurity(config);
        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(_replyTopic);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = consumer.Consume(cancellationToken);
                if (result?.Message is null)
                    continue;
                var correlationId = GetHeader(result.Message.Headers, HeaderCorrelationId) ?? result.Message.Key;
                if (correlationId is not null && _pending.TryGetValue(correlationId, out var tcs))
                    tcs.TrySetResult(result.Message.Value);
            }
        }
        catch (OperationCanceledException) { /* 正常关闭 */ }
        catch (Exception ex)
        {
            RaiseError(ex, "Kafka.ReplyLoop");
        }
        finally
        {
            try { consumer.Close(); } catch { /* ignore */ }
        }
    }

    private async Task ConsumeLoopAsync(string channel, string groupId, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = groupId,
            ClientId = $"{_clientId}-{channel}",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true
        };
        ApplySecurity(config);
        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(channel);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? result;
                try
                {
                    result = consumer.Consume(cancellationToken);
                }
                catch (ConsumeException ex)
                {
                    RaiseError(ex, "Kafka.Consume");
                    continue;
                }
                if (result?.Message is null)
                    continue;

                var msg = new TransportMessage
                {
                    Channel = channel,
                    Body = result.Message.Value,
                    ContentType = "application/json",
                    CorrelationId = GetHeader(result.Message.Headers, HeaderCorrelationId) ?? result.Message.Key ?? Guid.NewGuid().ToString("N")
                };
                RaiseMessageReceived(msg);
                try
                {
                    await handler(msg, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RaiseError(ex, "Kafka.Handler");
                }
            }
        }
        catch (OperationCanceledException) { /* 正常关闭 */ }
        finally
        {
            try { consumer.Close(); } catch { /* ignore */ }
        }
    }

    private void ApplySecurity<T>(T config) where T : ClientConfig
    {
        if (_options.Endpoint.UseTls || _options.Tls.Enabled)
            config.SecurityProtocol = _options.Auth.Type is MesAuthType.UsernamePassword or MesAuthType.Basic
                ? SecurityProtocol.SaslSsl
                : SecurityProtocol.Ssl;

        if (_options.Auth.Type is MesAuthType.UsernamePassword or MesAuthType.Basic)
        {
            config.SecurityProtocol ??= SecurityProtocol.SaslPlaintext;
            config.SaslMechanism = SaslMechanism.Plain;
            config.SaslUsername = _options.Auth.Username;
            config.SaslPassword = _options.Auth.Password;
        }
    }

    private static string? GetHeader(Headers? headers, string key)
    {
        if (headers is null) return null;
        return headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }

    private void FailPending(Exception ex)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetException(ex);
        }
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _loop;
        public Subscription(CancellationTokenSource cts, Task loop) { _cts = cts; _loop = loop; }
        public async ValueTask DisposeAsync()
        {
            try { _cts.Cancel(); } catch { /* ignore */ }
            try { await _loop.ConfigureAwait(false); } catch { /* ignore */ }
            _cts.Dispose();
        }
    }
}
