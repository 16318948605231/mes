using Mes.Core.Enums;
using Mes.Core.Serialization;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.InMemory;

/// <summary>
/// 进程内回环传输：把请求/发布/订阅路由到 <see cref="InMemoryMesServer"/>。
/// </summary>
public sealed class InMemoryTransport : MesTransportBase
{
    private readonly InMemoryMesServer _server;
    private readonly IMesSerializer _serializer;

    /// <summary>构造。</summary>
    public InMemoryTransport(InMemoryMesServer server, IMesSerializer? serializer = null, string? name = null, ILogger? logger = null)
        : base(name ?? "InMemory", logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _serializer = serializer ?? new JsonMesSerializer();
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.InMemory;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities => MesTransportCapabilities.All;

    /// <inheritdoc />
    protected override Task DoConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override Task DoDisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var r = _server.Handle(request);
        if (r.Error is not null && (r.StatusCode < 200 || r.StatusCode >= 300))
        {
            return Task.FromResult(new TransportResponse
            {
                Success = false,
                StatusCode = r.StatusCode,
                ErrorMessage = r.Error,
                CorrelationId = request.CorrelationId
            });
        }

        var body = r.Result is null ? null : _serializer.Serialize(r.Result);
        return Task.FromResult(new TransportResponse
        {
            Success = r.StatusCode is >= 200 and < 300,
            StatusCode = r.StatusCode,
            Body = body,
            ContentType = _serializer.ContentType,
            CorrelationId = request.CorrelationId
        });
    }

    /// <inheritdoc />
    protected override Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
        => _server.PublishAsync(message, cancellationToken);

    /// <inheritdoc />
    protected override Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        var wrapped = async (TransportMessage msg, CancellationToken c) =>
        {
            RaiseMessageReceived(msg);
            await handler(msg, c).ConfigureAwait(false);
        };
        var sub = _server.Subscribe(channel, wrapped);
        return Task.FromResult<IAsyncDisposable>(new AsyncDisposable(sub));
    }

    private sealed class AsyncDisposable : IAsyncDisposable
    {
        private readonly IDisposable _inner;
        public AsyncDisposable(IDisposable inner) => _inner = inner;

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
