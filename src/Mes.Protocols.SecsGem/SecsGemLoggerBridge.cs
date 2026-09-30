using Microsoft.Extensions.Logging;
using Secs4Net;

namespace Mes.Protocols.SecsGem;

/// <summary>
/// 将 <see cref="ISecsGemLogger"/> 适配到 <see cref="ILogger"/>。
/// </summary>
internal sealed class SecsGemLoggerBridge(ILogger logger) : ISecsGemLogger
{
    private readonly ILogger _logger = logger;

    public void Debug(string msg) => _logger.LogDebug("{Message}", msg);
    public void Info(string msg) => _logger.LogInformation("{Message}", msg);
    public void Warning(string msg) => _logger.LogWarning("{Message}", msg);
    public void Error(string msg) => _logger.LogError("{Message}", msg);
    public void Error(string msg, Exception ex) => _logger.LogError(ex, "{Message}", msg);
    public void Error(string msg, SecsMessage? message, Exception? ex)
        => _logger.LogError(ex, "{Message} ({SecsMessage})", msg, message?.Name);
    public void MessageIn(SecsMessage? msg, int id)
        => _logger.LogTrace("SECS <= {Name} (id={Id})", msg?.Name, id);
    public void MessageOut(SecsMessage? msg, int id)
        => _logger.LogTrace("SECS => {Name} (id={Id})", msg?.Name, id);
}
