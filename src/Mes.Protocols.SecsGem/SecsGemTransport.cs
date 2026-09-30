using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Exceptions;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secs4Net;

namespace Mes.Protocols.SecsGem;

/// <summary>
/// SECS/GEM（HSMS）传输，基于 Secs4Net。通道模板承载 <c>S{stream}F{function}</c>（例如
/// <c>S6F11</c>）；请求体（JSON 文本）以 ASCII <see cref="Item"/> 承载，回复消息的数据项按
/// ASCII 文本或 SML 文本返回。设备侧收到的一级消息（如事件/报警）通过订阅与
/// <see cref="MesTransportBase.MessageReceived"/> 事件推送。
/// </summary>
public sealed class SecsGemTransport : MesTransportBase
{
    private static readonly Regex SfRegex = new(@"S(\d+)F(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly MesOptions _options;
    private readonly ILogger _logger;
    private readonly ISecsGemLogger _secsLogger;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<TransportMessage, CancellationToken, Task>>> _subscribers = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _autoReplyPrimary;

    private HsmsConnection? _connection;
    private Secs4Net.SecsGem? _secsGem;
    private CancellationTokenSource? _cts;
    private Task? _readerTask;

    /// <summary>构造。</summary>
    public SecsGemTransport(MesOptions options, ILoggerFactory? loggerFactory = null)
        : base(options?.Name ?? "SecsGem", loggerFactory?.CreateLogger<SecsGemTransport>())
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SecsGemTransport>();
        _secsLogger = new SecsGemLoggerBridge(_logger);
        _autoReplyPrimary = !string.Equals(_options.GetProperty("AutoReplyPrimary"), "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.SecsGem;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities
        => MesTransportCapabilities.Request | MesTransportCapabilities.Publish | MesTransportCapabilities.Subscribe;

    /// <inheritdoc />
    protected override async Task DoConnectAsync(CancellationToken cancellationToken)
    {
        var (host, port) = ParseEndpoint();
        var active = !string.Equals(_options.GetProperty("Mode"), "Passive", StringComparison.OrdinalIgnoreCase);
        ushort deviceId = ushort.TryParse(_options.GetProperty("DeviceId"), out var d) ? d : (ushort)0;

        var secsOptions = new SecsGemOptions
        {
            IpAddress = host,
            Port = port,
            IsActive = active,
            DeviceId = deviceId
        };
        ApplyTimer(_options.GetProperty("T3"), v => secsOptions.T3 = v);
        ApplyTimer(_options.GetProperty("T5"), v => secsOptions.T5 = v);
        ApplyTimer(_options.GetProperty("T6"), v => secsOptions.T6 = v);
        ApplyTimer(_options.GetProperty("T7"), v => secsOptions.T7 = v);
        ApplyTimer(_options.GetProperty("T8"), v => secsOptions.T8 = v);
        ApplyTimer(_options.GetProperty("LinkTestInterval"), v => secsOptions.LinkTestInterval = v);

        var wrapped = Options.Create(secsOptions);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connection = new HsmsConnection(wrapped, _secsLogger);
        _secsGem = new Secs4Net.SecsGem(wrapped, _connection, _secsLogger);
        _connection.Start(_cts.Token);

        // 主动模式下等待进入 Selected 状态；被动模式监听、立即返回。
        if (active)
        {
            var timeout = int.TryParse(_options.GetProperty("ConnectTimeoutMs"), out var ms) ? ms : 15000;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                while (_connection.State != ConnectionState.Selected)
                    await Task.Delay(100, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MesConnectionException($"SECS/GEM 在 {timeout}ms 内未能进入 Selected 状态（当前 {_connection.State}）。");
            }
        }

        _readerTask = Task.Run(() => ReadPrimaryMessagesAsync(_cts.Token));
    }

    /// <inheritdoc />
    protected override async Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        if (_readerTask is not null)
        {
            try { await _readerTask.ConfigureAwait(false); } catch { /* 忽略 */ }
            _readerTask = null;
        }

        _secsGem?.Dispose();
        _secsGem = null;

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        _cts?.Dispose();
        _cts = null;
    }

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        if (_secsGem is null)
            throw new MesTransportException("SECS/GEM 未连接。");

        var (s, f) = ParseStreamFunction(request.Channel);
        using var message = new SecsMessage(s, f, replyExpected: true)
        {
            SecsItem = BuildItem(request.Body)
        };

        var reply = await _secsGem.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = ItemToBytes(reply?.SecsItem);
        return TransportResponse.Ok(body, 200, "application/json");
    }

    /// <inheritdoc />
    protected override async Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        if (_secsGem is null)
            throw new MesTransportException("SECS/GEM 未连接。");

        var (s, f) = ParseStreamFunction(message.Channel);
        using var secs = new SecsMessage(s, f, replyExpected: false)
        {
            SecsItem = BuildItem(message.Body)
        };
        await _secsGem.SendAsync(secs, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        var bucket = _subscribers.GetOrAdd(channel, _ => new ConcurrentDictionary<Guid, Func<TransportMessage, CancellationToken, Task>>());
        var id = Guid.NewGuid();
        bucket[id] = handler;
        IAsyncDisposable disposable = new Subscription(() =>
        {
            if (_subscribers.TryGetValue(channel, out var b))
                b.TryRemove(id, out _);
        });
        return Task.FromResult(disposable);
    }

    private async Task ReadPrimaryMessagesAsync(CancellationToken cancellationToken)
    {
        if (_secsGem is null)
            return;
        try
        {
            await foreach (var primary in _secsGem.GetPrimaryMessageAsync(cancellationToken).ConfigureAwait(false))
            {
                var pm = primary.PrimaryMessage;
                var channel = $"S{pm.S}F{pm.F}";
                var message = new TransportMessage
                {
                    Channel = channel,
                    Body = ItemToBytes(pm.SecsItem),
                    ContentType = "application/json"
                };

                RaiseMessageReceived(message);
                await DispatchAsync(channel, message, cancellationToken).ConfigureAwait(false);

                if (_autoReplyPrimary && pm.ReplyExpected)
                {
                    try
                    {
                        using var secondary = new SecsMessage(pm.S, (byte)(pm.F + 1), replyExpected: false) { SecsItem = Item.L() };
                        await primary.TryReplyAsync(secondary, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "SECS/GEM 自动回复失败。");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch (Exception ex)
        {
            RaiseError(ex, "SecsPrimaryReader");
        }
    }

    private async Task DispatchAsync(string channel, TransportMessage message, CancellationToken cancellationToken)
    {
        await InvokeBucketAsync(channel, message, cancellationToken).ConfigureAwait(false);
        await InvokeBucketAsync("*", message, cancellationToken).ConfigureAwait(false);
    }

    private async Task InvokeBucketAsync(string key, TransportMessage message, CancellationToken cancellationToken)
    {
        if (!_subscribers.TryGetValue(key, out var bucket))
            return;
        foreach (var handler in bucket.Values)
        {
            try { await handler(message, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { RaiseError(ex, "SecsSubscriber"); }
        }
    }

    private (byte S, byte F) ParseStreamFunction(string channel)
    {
        var m = SfRegex.Match(channel ?? string.Empty);
        if (!m.Success)
            throw new MesTransportException($"SECS/GEM 通道 '{channel}' 不合法，应为 SxFy（例如 S6F11）。");
        return ((byte)int.Parse(m.Groups[1].Value), (byte)int.Parse(m.Groups[2].Value));
    }

    private static Item BuildItem(byte[]? body)
        => body is null || body.Length == 0 ? Item.L() : Item.A(Encoding.UTF8.GetString(body));

    private static byte[] ItemToBytes(Item? item)
    {
        if (item is null)
            return Array.Empty<byte>();
        if (item.Format == SecsFormat.ASCII)
            return Encoding.UTF8.GetBytes(item.GetString());
        return Encoding.UTF8.GetBytes(item.ToString() ?? string.Empty);
    }

    private (string Host, int Port) ParseEndpoint()
    {
        var baseAddress = _options.Endpoint.BaseAddress;
        if (!string.IsNullOrWhiteSpace(baseAddress))
        {
            var s = baseAddress!;
            var schemeIdx = s.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx >= 0)
                s = s[(schemeIdx + 3)..];
            s = s.TrimEnd('/');
            var parts = s.Split(':', 2);
            var host = parts[0];
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5000;
            return (host, port);
        }
        return (_options.Endpoint.Host ?? "127.0.0.1", _options.Endpoint.Port ?? 5000);
    }

    private static void ApplyTimer(string? value, Action<int> apply)
    {
        if (int.TryParse(value, out var v))
            apply(v);
    }

    private sealed class Subscription(Action dispose) : IAsyncDisposable
    {
        private readonly Action _dispose = dispose;
        public ValueTask DisposeAsync()
        {
            _dispose();
            return ValueTask.CompletedTask;
        }
    }
}
