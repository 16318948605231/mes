using Mes.Core.Enums;
using Mes.Core.Events;
using Mes.Core.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mes.Core.Transport;

/// <summary>
/// 传输实现的抽象基类：集中处理状态机、事件触发、能力校验与日志，
/// 子类只需实现受保护的 <c>Do*</c> 模板方法。
/// </summary>
public abstract class MesTransportBase : IMesTransport
{
    private int _state = (int)MesConnectionState.Disconnected;
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    /// <summary>构造。</summary>
    protected MesTransportBase(string name, ILogger? logger = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? GetType().Name : name;
        Logger = logger ?? NullLogger.Instance;
    }

    /// <summary>日志器。</summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public abstract MesProtocolKind Protocol { get; }

    /// <inheritdoc />
    public abstract MesTransportCapabilities Capabilities { get; }

    /// <inheritdoc />
    public MesConnectionState State => (MesConnectionState)Volatile.Read(ref _state);

    /// <inheritdoc />
    public event EventHandler<MesConnectionStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<TransportMessageEventArgs>? MessageReceived;

    /// <inheritdoc />
    public event EventHandler<MesErrorEventArgs>? Error;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == MesConnectionState.Connected)
                return;

            SetState(MesConnectionState.Connecting);
            try
            {
                await DoConnectAsync(cancellationToken).ConfigureAwait(false);
                SetState(MesConnectionState.Connected);
            }
            catch (Exception ex)
            {
                SetState(MesConnectionState.Faulted, ex.Message);
                RaiseError(ex, "ConnectAsync");
                throw new MesConnectionException($"传输 '{Name}' 连接失败: {ex.Message}", ex);
            }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is MesConnectionState.Disconnected or MesConnectionState.Closed)
                return;

            try
            {
                await DoDisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaiseError(ex, "DisconnectAsync");
            }
            finally
            {
                SetState(MesConnectionState.Disconnected);
            }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TransportResponse> RequestAsync(TransportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCapability(MesTransportCapabilities.Request);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await DoRequestAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            response.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            response.CorrelationId ??= request.CorrelationId;
            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RaiseError(ex, "RequestAsync");
            return new TransportResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                CorrelationId = request.CorrelationId,
                ElapsedMilliseconds = sw.ElapsedMilliseconds
            };
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        EnsureCapability(MesTransportCapabilities.Publish);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await DoPublishAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> SubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);
        EnsureCapability(MesTransportCapabilities.Subscribe);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await DoSubscribeAsync(channel, handler, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>确保已连接；未连接时自动连接。</summary>
    protected async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (State != MesConnectionState.Connected)
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>校验能力，不支持则抛出。</summary>
    protected void EnsureCapability(MesTransportCapabilities capability)
    {
        if ((Capabilities & capability) != capability)
            throw new MesTransportException($"传输 '{Name}'（{Protocol}）不支持 {capability} 能力。");
    }

    /// <summary>设置状态并触发事件。</summary>
    protected void SetState(MesConnectionState newState, string? reason = null)
    {
        var previous = (MesConnectionState)Interlocked.Exchange(ref _state, (int)newState);
        if (previous == newState)
            return;

        Logger.LogDebug("传输 '{Name}' 状态: {Previous} -> {Current} ({Reason})", Name, previous, newState, reason);
        try
        {
            StateChanged?.Invoke(this, new MesConnectionStateChangedEventArgs(previous, newState, reason));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "StateChanged 事件处理器抛出异常。");
        }
    }

    /// <summary>触发收到消息事件。</summary>
    protected void RaiseMessageReceived(TransportMessage message)
    {
        try
        {
            MessageReceived?.Invoke(this, new TransportMessageEventArgs(message));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MessageReceived 事件处理器抛出异常。");
        }
    }

    /// <summary>触发错误事件。</summary>
    protected void RaiseError(Exception exception, string? context = null)
    {
        try
        {
            Error?.Invoke(this, new MesErrorEventArgs(exception, context));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error 事件处理器抛出异常。");
        }
    }

    // ---- 子类模板方法 ----

    /// <summary>建立连接的具体实现。</summary>
    protected abstract Task DoConnectAsync(CancellationToken cancellationToken);

    /// <summary>断开连接的具体实现。</summary>
    protected abstract Task DoDisconnectAsync(CancellationToken cancellationToken);

    /// <summary>请求/响应的具体实现。默认抛出不支持。</summary>
    protected virtual Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
        => throw new MesTransportException($"传输 '{Name}' 未实现 RequestAsync。");

    /// <summary>发布的具体实现。默认抛出不支持。</summary>
    protected virtual Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
        => throw new MesTransportException($"传输 '{Name}' 未实现 PublishAsync。");

    /// <summary>订阅的具体实现。默认抛出不支持。</summary>
    protected virtual Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
        => throw new MesTransportException($"传输 '{Name}' 未实现 SubscribeAsync。");

    /// <inheritdoc />
    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        try
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 释放阶段忽略异常
        }
        SetState(MesConnectionState.Closed);
        _connectGate.Dispose();
    }
}
