using System.Collections.Concurrent;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Exceptions;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Mes.Protocols.Amqp;

/// <summary>
/// AMQP (RabbitMQ) 传输：上报类走主题交换机发布；查询类基于“回复队列 + 关联标识”实现请求/响应（RPC）。
/// 使用 RabbitMQ.Client 7.x 的全异步 API（<see cref="IChannel"/> / <see cref="AsyncEventingBasicConsumer"/>）。
/// </summary>
public sealed class AmqpTransport : MesTransportBase
{
    private readonly MesOptions _options;
    private readonly string _exchange;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]?>> _pending = new();

    private IConnection? _connection;
    private IChannel? _channel;
    private string? _replyQueue;
    private bool _replyReady;

    /// <summary>构造。</summary>
    public AmqpTransport(MesOptions options, ILogger? logger = null)
        : base(options?.Name ?? "Amqp", logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _exchange = _options.GetProperty("Exchange") ?? "mes";
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.Amqp;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities => MesTransportCapabilities.All;

    /// <inheritdoc />
    protected override async Task DoConnectAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Endpoint.Host ?? "localhost",
            Port = _options.Endpoint.Port ?? 5672,
            VirtualHost = _options.GetProperty("VirtualHost") ?? "/"
        };
        if (_options.Auth.Type is MesAuthType.UsernamePassword or MesAuthType.Basic)
        {
            factory.UserName = _options.Auth.Username ?? factory.UserName;
            factory.Password = _options.Auth.Password ?? factory.Password;
        }
        if (_options.Endpoint.UseTls || _options.Tls.Enabled)
        {
            factory.Ssl = new SslOption
            {
                Enabled = true,
                ServerName = _options.Endpoint.Host ?? "localhost"
            };
            if (_options.Tls.AllowUntrustedCertificates)
            {
                factory.Ssl.AcceptablePolicyErrors =
                    System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors |
                    System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch;
            }
        }

        _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await _channel.ExchangeDeclareAsync(_exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        FailPending(new MesTransportException("AMQP 连接已关闭。"));
        _replyReady = false;
        _replyQueue = null;

        if (_channel is not null)
        {
            try { await _channel.CloseAsync(cancellationToken).ConfigureAwait(false); } catch { /* ignore */ }
            await _channel.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }
        if (_connection is not null)
        {
            try { await _connection.CloseAsync(cancellationToken).ConfigureAwait(false); } catch { /* ignore */ }
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    /// <inheritdoc />
    protected override async Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new MesTransportException("AMQP 通道未初始化。");
        var props = new BasicProperties { CorrelationId = message.CorrelationId, ContentType = message.ContentType };
        await channel.BasicPublishAsync(_exchange, message.Channel, mandatory: false, basicProperties: props,
            body: message.Body ?? Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new MesTransportException("AMQP 通道未初始化。");
        await EnsureReplyQueueAsync(cancellationToken).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.CorrelationId] = tcs;
        try
        {
            var props = new BasicProperties
            {
                CorrelationId = request.CorrelationId,
                ReplyTo = _replyQueue,
                ContentType = request.ContentType ?? "application/json"
            };
            await channel.BasicPublishAsync(_exchange, request.Channel, mandatory: false, basicProperties: props,
                body: request.Body ?? Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);

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
            return TransportResponse.Fail($"AMQP 请求超时（correlationId={request.CorrelationId}）。");
        }
        finally
        {
            _pending.TryRemove(request.CorrelationId, out _);
        }
    }

    /// <inheritdoc />
    protected override async Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        var connection = _connection ?? throw new MesTransportException("AMQP 连接未初始化。");
        var subChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await subChannel.ExchangeDeclareAsync(_exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var queue = await subChannel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true,
            autoDelete: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await subChannel.QueueBindAsync(queue.QueueName, _exchange, channel, cancellationToken: cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(subChannel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var msg = new TransportMessage
            {
                Channel = ea.RoutingKey,
                Body = ea.Body.ToArray(),
                ContentType = ea.BasicProperties.ContentType ?? "application/json",
                CorrelationId = ea.BasicProperties.CorrelationId ?? Guid.NewGuid().ToString("N")
            };
            RaiseMessageReceived(msg);
            try
            {
                await handler(msg, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaiseError(ex, "Amqp.Handler");
            }
        };
        var consumerTag = await subChannel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new Subscription(subChannel, consumerTag);
    }

    private async Task EnsureReplyQueueAsync(CancellationToken cancellationToken)
    {
        if (_replyReady)
            return;
        var channel = _channel ?? throw new MesTransportException("AMQP 通道未初始化。");
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true,
            autoDelete: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        _replyQueue = queue.QueueName;

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            var correlationId = ea.BasicProperties.CorrelationId;
            if (correlationId is not null && _pending.TryGetValue(correlationId, out var tcs))
                tcs.TrySetResult(ea.Body.ToArray());
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(_replyQueue, autoAck: true, consumer, cancellationToken: cancellationToken).ConfigureAwait(false);
        _replyReady = true;
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
        private readonly IChannel _channel;
        private readonly string _consumerTag;
        public Subscription(IChannel channel, string consumerTag) { _channel = channel; _consumerTag = consumerTag; }
        public async ValueTask DisposeAsync()
        {
            try { await _channel.BasicCancelAsync(_consumerTag).ConfigureAwait(false); } catch { /* ignore */ }
            try { await _channel.CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
            await _channel.DisposeAsync().ConfigureAwait(false);
        }
    }
}
