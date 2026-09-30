namespace Mes.Core.Exceptions;

/// <summary>
/// 框架所有异常的基类。
/// </summary>
public class MesException : Exception
{
    /// <summary>错误码。</summary>
    public string Code { get; }

    /// <summary>关联标识。</summary>
    public string? CorrelationId { get; init; }

    /// <summary>构造。</summary>
    public MesException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}

/// <summary>连接相关异常。</summary>
public sealed class MesConnectionException : MesException
{
    /// <summary>构造。</summary>
    public MesConnectionException(string message, Exception? innerException = null)
        : base(Common.MesResultCodes.ConnectionFailed, message, innerException) { }
}

/// <summary>传输相关异常。</summary>
public sealed class MesTransportException : MesException
{
    /// <summary>传输层原始状态码（若有，例如 HTTP 状态码）。</summary>
    public int? StatusCode { get; init; }

    /// <summary>构造。</summary>
    public MesTransportException(string message, Exception? innerException = null)
        : base(Common.MesResultCodes.TransportError, message, innerException) { }
}

/// <summary>超时异常。</summary>
public sealed class MesTimeoutException : MesException
{
    /// <summary>构造。</summary>
    public MesTimeoutException(string message, Exception? innerException = null)
        : base(Common.MesResultCodes.Timeout, message, innerException) { }
}

/// <summary>序列化异常。</summary>
public sealed class MesSerializationException : MesException
{
    /// <summary>构造。</summary>
    public MesSerializationException(string message, Exception? innerException = null)
        : base(Common.MesResultCodes.SerializationError, message, innerException) { }
}

/// <summary>配置异常。</summary>
public sealed class MesConfigurationException : MesException
{
    /// <summary>构造。</summary>
    public MesConfigurationException(string message, Exception? innerException = null)
        : base(Common.MesResultCodes.ValidationError, message, innerException) { }
}

/// <summary>操作映射/调用异常。</summary>
public sealed class MesOperationException : MesException
{
    /// <summary>操作键。</summary>
    public string? OperationKey { get; init; }

    /// <summary>构造。</summary>
    public MesOperationException(string code, string message, Exception? innerException = null)
        : base(code, message, innerException) { }
}
