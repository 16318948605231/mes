namespace Mes.Core.Common;

/// <summary>
/// 统一的操作结果（无返回值）。所有对外的高层方法均返回该类型或其泛型版本，
/// 以避免异常穿透并携带丰富的诊断信息。
/// </summary>
public class MesResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>业务/传输错误码（成功时通常为 <c>"OK"</c>）。</summary>
    public string Code { get; init; } = MesResultCodes.Ok;

    /// <summary>可读消息（成功或失败说明）。</summary>
    public string? Message { get; init; }

    /// <summary>底层异常（若有）。</summary>
    public Exception? Exception { get; init; }

    /// <summary>关联/追踪标识，便于日志串联。</summary>
    public string? CorrelationId { get; init; }

    /// <summary>耗时（毫秒）。</summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>附加元数据（如原始状态码、响应头等）。</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>是否失败。</summary>
    public bool IsFailure => !Success;

    /// <summary>创建成功结果。</summary>
    public static MesResult Ok(string? message = null, string? correlationId = null)
        => new() { Success = true, Code = MesResultCodes.Ok, Message = message, CorrelationId = correlationId };

    /// <summary>创建失败结果。</summary>
    public static MesResult Fail(string code, string? message = null, Exception? exception = null, string? correlationId = null)
        => new() { Success = false, Code = code, Message = message ?? exception?.Message, Exception = exception, CorrelationId = correlationId };

    /// <summary>从异常创建失败结果。</summary>
    public static MesResult FromException(Exception exception, string? code = null, string? correlationId = null)
        => Fail(code ?? MesResultCodes.Exception, exception.Message, exception, correlationId);

    /// <inheritdoc />
    public override string ToString()
        => Success ? $"OK ({ElapsedMilliseconds}ms)" : $"FAIL[{Code}] {Message}";
}

/// <summary>
/// 带返回值的操作结果。
/// </summary>
/// <typeparam name="T">返回值类型。</typeparam>
public sealed class MesResult<T> : MesResult
{
    /// <summary>返回值（失败时通常为默认值）。</summary>
    public T? Value { get; init; }

    /// <summary>创建带值的成功结果。</summary>
    public static MesResult<T> Ok(T value, string? message = null, string? correlationId = null)
        => new() { Success = true, Code = MesResultCodes.Ok, Value = value, Message = message, CorrelationId = correlationId };

    /// <summary>创建失败结果。</summary>
    public static new MesResult<T> Fail(string code, string? message = null, Exception? exception = null, string? correlationId = null)
        => new() { Success = false, Code = code, Message = message ?? exception?.Message, Exception = exception, CorrelationId = correlationId };

    /// <summary>从异常创建失败结果。</summary>
    public static new MesResult<T> FromException(Exception exception, string? code = null, string? correlationId = null)
        => Fail(code ?? MesResultCodes.Exception, exception.Message, exception, correlationId);
}
