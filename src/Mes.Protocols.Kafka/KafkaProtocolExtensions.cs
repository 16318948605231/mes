using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Kafka;

/// <summary>
/// Kafka 传输工厂。
/// </summary>
public sealed class KafkaTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.Kafka;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new KafkaTransport(options, loggerFactory.CreateLogger<KafkaTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options)
        => MesOperationCatalog.CreateKafkaDefaults(options.GetProperty("TopicPrefix") ?? "mes");
}

/// <summary>
/// Kafka Provider 注册扩展。
/// </summary>
public static class KafkaProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用 Kafka 协议。</summary>
    public static MesClientBuilder UseKafka(this MesClientBuilder builder, string bootstrapServers, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);
        builder.AddTransportFactory(new KafkaTransportFactory());
        builder.Options.Protocol = MesProtocolKind.Kafka;
        builder.Options.SetProperty("BootstrapServers", bootstrapServers);
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册 Kafka 传输工厂。</summary>
    public static IServiceCollection AddKafkaProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new KafkaTransportFactory());
        return services;
    }
}
