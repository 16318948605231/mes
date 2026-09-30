using Mes.Core.Enums;
using Mes.Core.Models;
using Mes.Core.Transport;

namespace Mes.Core.Events;

/// <summary>
/// 连接状态变化事件参数。
/// </summary>
public sealed class MesConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>构造。</summary>
    public MesConnectionStateChangedEventArgs(MesConnectionState previous, MesConnectionState current, string? reason = null)
    {
        Previous = previous;
        Current = current;
        Reason = reason;
    }

    /// <summary>变化前状态。</summary>
    public MesConnectionState Previous { get; }

    /// <summary>变化后状态。</summary>
    public MesConnectionState Current { get; }

    /// <summary>变化原因（可选）。</summary>
    public string? Reason { get; }

    /// <summary>时间戳。</summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;
}

/// <summary>
/// 传输层收到消息事件参数（订阅/推送）。
/// </summary>
public sealed class TransportMessageEventArgs : EventArgs
{
    /// <summary>构造。</summary>
    public TransportMessageEventArgs(TransportMessage message) => Message = message;

    /// <summary>消息。</summary>
    public TransportMessage Message { get; }
}

/// <summary>
/// 错误事件参数。
/// </summary>
public sealed class MesErrorEventArgs : EventArgs
{
    /// <summary>构造。</summary>
    public MesErrorEventArgs(Exception exception, string? context = null)
    {
        Exception = exception;
        Context = context;
    }

    /// <summary>异常。</summary>
    public Exception Exception { get; }

    /// <summary>上下文说明。</summary>
    public string? Context { get; }

    /// <summary>时间戳。</summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;
}

/// <summary>
/// 高层客户端收到业务消息事件参数。
/// </summary>
public sealed class MesMessageReceivedEventArgs : EventArgs
{
    /// <summary>构造。</summary>
    public MesMessageReceivedEventArgs(string channel, TransportMessage message)
    {
        Channel = channel;
        Message = message;
    }

    /// <summary>频道/主题。</summary>
    public string Channel { get; }

    /// <summary>底层传输消息。</summary>
    public TransportMessage Message { get; }
}

/// <summary>
/// 报警事件参数（收到 MES/设备下发的报警）。
/// </summary>
public sealed class MesAlarmEventArgs : EventArgs
{
    /// <summary>构造。</summary>
    public MesAlarmEventArgs(Alarm alarm) => Alarm = alarm;

    /// <summary>报警。</summary>
    public Alarm Alarm { get; }
}

/// <summary>
/// 检测结果已上报事件参数。
/// </summary>
public sealed class MesInspectionReportedEventArgs : EventArgs
{
    /// <summary>构造。</summary>
    public MesInspectionReportedEventArgs(InspectionResult result, bool success)
    {
        Result = result;
        Success = success;
    }

    /// <summary>被上报的检测结果。</summary>
    public InspectionResult Result { get; }

    /// <summary>上报是否成功。</summary>
    public bool Success { get; }
}
