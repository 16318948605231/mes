using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Mqtt;

/// <summary>
/// MQTT 传输工厂。
/// </summary>
public sealed class MqttTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.Mqtt;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new MqttTransport(options, loggerFactory.CreateLogger<MqttTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options)
        => MesOperationCatalog.CreateMqttDefaults(options.GetProperty("TopicPrefix") ?? "mes");
}

/// <summary>
/// MQTT Provider 注册扩展。
/// </summary>
public static class MqttProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用 MQTT 协议。</summary>
    public static MesClientBuilder UseMqtt(this MesClientBuilder builder, string host, int? port = null, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTransportFactory(new MqttTransportFactory());
        builder.Options.Protocol = MesProtocolKind.Mqtt;
        builder.Options.Endpoint.Host = host;
        builder.Options.Endpoint.Port = port;
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册 MQTT 传输工厂。</summary>
    public static IServiceCollection AddMqttProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new MqttTransportFactory());
        return services;
    }
}
