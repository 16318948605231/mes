using Mes.Core.Enums;

namespace Mes.Core.Operations;

/// <summary>
/// 操作绑定：把一个高层操作键映射到具体的传输通道/端点及调用方式。
/// </summary>
public sealed class MesOperationBinding
{
    /// <summary>操作键。</summary>
    public string OperationKey { get; set; } = string.Empty;

    /// <summary>
    /// 通道模板（支持 <c>{name}</c> 占位符）。
    /// REST=URL 模板；MQTT=主题模板；数据库=命令/存储过程名；文件=文件名模板；OPC UA=节点/方法。
    /// </summary>
    public string ChannelTemplate { get; set; } = string.Empty;

    /// <summary>动词（REST 的 HTTP 方法；其它传输可用于区分读/写/调用/发布）。</summary>
    public string? Verb { get; set; }

    /// <summary>负载格式。</summary>
    public MesPayloadFormat Format { get; set; } = MesPayloadFormat.Json;

    /// <summary>
    /// 是否为请求/响应语义。false 表示发布/上报（fire-and-forget），
    /// 高层客户端将调用传输的 PublishAsync 而非 RequestAsync。
    /// </summary>
    public bool RequestResponse { get; set; } = true;

    /// <summary>默认请求头。</summary>
    public IDictionary<string, string> DefaultHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>默认参数。</summary>
    public IDictionary<string, object?> DefaultParameters { get; set; } = new Dictionary<string, object?>();

    /// <summary>QoS（发布语义时用于 MQTT）。</summary>
    public int Qos { get; set; }

    /// <summary>是否保留（发布语义时用于 MQTT）。</summary>
    public bool Retain { get; set; }
}
