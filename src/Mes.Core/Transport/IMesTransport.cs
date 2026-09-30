using Mes.Core.Enums;
using Mes.Core.Events;

namespace Mes.Core.Transport;

/// <summary>
/// 传输/协议抽象。每个协议（REST、MQTT、OPC UA、文件、数据库、SECS/GEM…）实现该接口。
/// 高层 <c>IMesClient</c> 只依赖本接口，从而做到协议无关。
/// </summary>
public interface IMesTransport : IAsyncDisposable
{
    /// <summary>传输名称（用于日志与多实例区分）。</summary>
    string Name { get; }

    /// <summary>协议种类。</summary>
    MesProtocolKind Protocol { get; }

    /// <summary>支持的能力。</summary>
    MesTransportCapabilities Capabilities { get; }

    /// <summary>当前连接状态。</summary>
    MesConnectionState State { get; }

    /// <summary>建立连接。</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>断开连接。</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>发送请求并等待响应（要求具备 <see cref="MesTransportCapabilities.Request"/> 能力）。</summary>
    Task<TransportResponse> RequestAsync(TransportRequest request, CancellationToken cancellationToken = default);

    /// <summary>发布消息（要求具备 <see cref="MesTransportCapabilities.Publish"/> 能力）。</summary>
    Task PublishAsync(TransportMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅频道（要求具备 <see cref="MesTransportCapabilities.Subscribe"/> 能力）。
    /// 返回可释放的订阅句柄；释放即取消订阅。
    /// </summary>
    Task<IAsyncDisposable> SubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default);

    /// <summary>连接状态变化事件。</summary>
    event EventHandler<MesConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>收到消息事件（订阅/推送）。</summary>
    event EventHandler<TransportMessageEventArgs>? MessageReceived;

    /// <summary>错误事件。</summary>
    event EventHandler<MesErrorEventArgs>? Error;
}
