namespace Mes.Core.Common;

/// <summary>
/// 标准结果码常量。业务码由具体 MES 定义，此处提供框架级通用码。
/// </summary>
public static class MesResultCodes
{
    /// <summary>成功。</summary>
    public const string Ok = "OK";

    /// <summary>未知异常。</summary>
    public const string Exception = "EXCEPTION";

    /// <summary>参数校验失败。</summary>
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>未连接。</summary>
    public const string NotConnected = "NOT_CONNECTED";

    /// <summary>连接失败。</summary>
    public const string ConnectionFailed = "CONNECTION_FAILED";

    /// <summary>请求超时。</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>已取消。</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>传输错误。</summary>
    public const string TransportError = "TRANSPORT_ERROR";

    /// <summary>序列化/反序列化错误。</summary>
    public const string SerializationError = "SERIALIZATION_ERROR";

    /// <summary>操作未映射（操作目录中找不到绑定）。</summary>
    public const string OperationNotMapped = "OPERATION_NOT_MAPPED";

    /// <summary>能力不支持（如向不支持订阅的传输订阅）。</summary>
    public const string NotSupported = "NOT_SUPPORTED";

    /// <summary>服务器返回的业务错误。</summary>
    public const string ServerError = "SERVER_ERROR";

    /// <summary>资源未找到。</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>未授权。</summary>
    public const string Unauthorized = "UNAUTHORIZED";
}

/// <summary>
/// 框架级常量。
/// </summary>
public static class MesConstants
{
    /// <summary>关联标识请求头名称。</summary>
    public const string CorrelationIdHeader = "X-Mes-Correlation-Id";

    /// <summary>默认字符编码名称。</summary>
    public const string DefaultEncoding = "utf-8";

    /// <summary>默认请求超时（毫秒）。</summary>
    public const int DefaultTimeoutMs = 30_000;
}
