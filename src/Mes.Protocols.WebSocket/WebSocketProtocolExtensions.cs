using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.WebSocket;

/// <summary>
/// WebSocket 传输工厂。
/// </summary>
public sealed class WebSocketTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.WebSocket;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new WebSocketTransport(options, logger: loggerFactory.CreateLogger<WebSocketTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options)
        => MesOperationCatalog.CreateWebSocketDefaults(options.GetProperty("TopicPrefix") ?? "mes");
}

/// <summary>
/// WebSocket Provider 注册扩展。
/// </summary>
public static class WebSocketProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用 WebSocket 协议。</summary>
    /// <param name="builder">构建器。</param>
    /// <param name="url">WebSocket 地址（ws:// 或 wss://）。</param>
    /// <param name="configure">可选的附加配置。</param>
    public static MesClientBuilder UseWebSocket(this MesClientBuilder builder, string url, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        builder.AddTransportFactory(new WebSocketTransportFactory());
        builder.Options.Protocol = MesProtocolKind.WebSocket;
        builder.Options.Endpoint.BaseAddress = url;
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册 WebSocket 传输工厂。</summary>
    public static IServiceCollection AddWebSocketProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new WebSocketTransportFactory());
        return services;
    }
}
