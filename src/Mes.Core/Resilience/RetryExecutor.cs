using Mes.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Mes.Core.Resilience;

/// <summary>
/// 轻量重试执行器：对瞬时故障（异常/超时）执行指数退避重试。
/// 不引入外部依赖；如需更强能力可在应用层叠加 Polly。
/// </summary>
public sealed class RetryExecutor
{
    private readonly MesRetryOptions _options;
    private readonly ILogger _logger;

    /// <summary>构造。</summary>
    public RetryExecutor(MesRetryOptions options, ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>
    /// 执行带重试的异步操作。<paramref name="shouldRetry"/> 决定某个结果是否需要重试。
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Func<T, bool>? shouldRetry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        int attempt = 0;
        int max = _options.Enabled ? _options.MaxRetries : 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await action(cancellationToken).ConfigureAwait(false);
                if (attempt >= max || shouldRetry is null || !shouldRetry(result))
                    return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < max)
            {
                _logger.LogWarning(ex, "第 {Attempt}/{Max} 次尝试失败，准备重试。", attempt + 1, max);
            }

            attempt++;
            var delay = ComputeDelay(attempt);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>计算第 N 次重试的退避延迟。</summary>
    public TimeSpan ComputeDelay(int attempt)
    {
        var ms = _options.BaseDelayMs * Math.Pow(_options.BackoffFactor, attempt - 1);
        ms = Math.Min(ms, _options.MaxDelayMs);
        // 加入抖动，避免惊群
        var jitter = Random.Shared.NextDouble() * 0.2 + 0.9; // 0.9~1.1
        return TimeSpan.FromMilliseconds(ms * jitter);
    }
}
