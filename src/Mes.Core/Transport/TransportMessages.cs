using Mes.Core.Enums;

namespace Mes.Core.Transport;

/// <summary>
/// 传输层请求（请求/响应语义）。协议无关：对 REST 为 HTTP 请求，
/// 对数据库为一次命令，对 OPC UA 为一次读写/方法调用等。
/// </summary>
public sealed class TransportRequest
{
    /// <summary>
    /// 目标通道/端点。语义由具体传输解释：
    /// REST=相对/绝对 URL；MQTT=主题；OPC UA=节点或方法；数据库=命令名/存储过程；文件=文件名模板。
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// 操作动词。REST 为 HTTP 方法（GET/POST/…）；其它传输可用于区分读/写/调用等。
    /// </summary>
    public string? Verb { get; set; }

    /// <summary>请求头/元数据。</summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>查询参数/命名参数。</summary>
    public IDictionary<string, object?> Parameters { get; set; } = new Dictionary<string, object?>();

    /// <summary>已序列化的请求体（与 <see cref="PayloadObject"/> 二选一）。</summary>
    public byte[]? Body { get; set; }

    /// <summary>未序列化的请求对象（由客户端在发送前序列化）。</summary>
    public object? PayloadObject { get; set; }

    /// <summary>请求体格式。</summary>
    public MesPayloadFormat Format { get; set; } = MesPayloadFormat.Json;

    /// <summary>内容类型。</summary>
    public string? ContentType { get; set; }

    /// <summary>关联标识。</summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>超时（毫秒），为空则使用传输默认值。</summary>
    public int? TimeoutMs { get; set; }

    /// <summary>期望的响应类型（供传输/客户端做反序列化提示，可空）。</summary>
    public Type? ExpectedResponseType { get; set; }
}

/// <summary>
/// 传输层响应。
/// </summary>
public sealed class TransportResponse
{
    /// <summary>是否成功（传输/协议层面）。</summary>
    public bool Success { get; set; }

    /// <summary>原始状态码（HTTP 状态码、数据库受影响行数、协议返回码等）。</summary>
    public int StatusCode { get; set; }

    /// <summary>响应头/元数据。</summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>响应体字节。</summary>
    public byte[]? Body { get; set; }

    /// <summary>内容类型。</summary>
    public string? ContentType { get; set; }

    /// <summary>关联标识。</summary>
    public string? CorrelationId { get; set; }

    /// <summary>错误消息（失败时）。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>将响应体按 UTF-8 解码为字符串。</summary>
    public string? BodyAsString()
        => Body is null ? null : System.Text.Encoding.UTF8.GetString(Body);

    /// <summary>创建成功响应。</summary>
    public static TransportResponse Ok(byte[]? body = null, int statusCode = 200, string? contentType = null)
        => new() { Success = true, StatusCode = statusCode, Body = body, ContentType = contentType };

    /// <summary>创建失败响应。</summary>
    public static TransportResponse Fail(string error, int statusCode = 0)
        => new() { Success = false, StatusCode = statusCode, ErrorMessage = error };
}

/// <summary>
/// 传输层消息（发布/订阅语义）。
/// </summary>
public sealed class TransportMessage
{
    /// <summary>频道/主题。</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>消息体字节。</summary>
    public byte[]? Body { get; set; }

    /// <summary>内容类型。</summary>
    public string? ContentType { get; set; }

    /// <summary>头/元数据。</summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>服务质量（0/1/2，MQTT 语义；其它传输可忽略）。</summary>
    public int Qos { get; set; }

    /// <summary>是否保留（MQTT 语义）。</summary>
    public bool Retain { get; set; }

    /// <summary>关联标识。</summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>时间戳。</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>将消息体按 UTF-8 解码为字符串。</summary>
    public string? BodyAsString()
        => Body is null ? null : System.Text.Encoding.UTF8.GetString(Body);

    /// <summary>由字符串创建消息。</summary>
    public static TransportMessage FromString(string channel, string body, string? contentType = "application/json")
        => new() { Channel = channel, Body = System.Text.Encoding.UTF8.GetBytes(body), ContentType = contentType };
}
