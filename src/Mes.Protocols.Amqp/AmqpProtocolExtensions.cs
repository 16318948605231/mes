using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Amqp;

/// <summary>
/// AMQP 传输工厂。
/// </summary>
public sealed class AmqpTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.Amqp;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new AmqpTransport(options, loggerFactory.CreateLogger<AmqpTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options)
        => MesOperationCatalog.CreateAmqpDefaults(options.GetProperty("TopicPrefix") ?? "mes");
}

/// <summary>
/// AMQP Provider 注册扩展。
/// </summary>
public static class AmqpProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用 AMQP (RabbitMQ) 协议。</summary>
    public static MesClientBuilder UseAmqp(this MesClientBuilder builder, string host, int? port = null, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        builder.AddTransportFactory(new AmqpTransportFactory());
        builder.Options.Protocol = MesProtocolKind.Amqp;
        builder.Options.Endpoint.Host = host;
        builder.Options.Endpoint.Port = port;
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册 AMQP 传输工厂。</summary>
    public static IServiceCollection AddAmqpProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new AmqpTransportFactory());
        return services;
    }
}
