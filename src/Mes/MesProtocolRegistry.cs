using Mes.Core.Client;
using Mes.Core.Enums;
using Mes.Protocols.Amqp;
using Mes.Protocols.Database;
using Mes.Protocols.FileDrop;
using Mes.Protocols.Grpc;
using Mes.Protocols.InMemory;
using Mes.Protocols.Kafka;
using Mes.Protocols.Mqtt;
using Mes.Protocols.OpcUa;
using Mes.Protocols.Rest;
using Mes.Protocols.SecsGem;
using Mes.Protocols.Soap;
using Mes.Protocols.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace Mes;

/// <summary>
/// 协议注册表：把 <see cref="MesProtocolKind"/> 映射到对应的传输工厂。
/// 聚合包引用了全部 Provider，因此可以按协议种类即时创建任意工厂，
/// 从而支撑“一行连接字符串自动选协议”和 <c>AddMes</c> 的零代码切换。
/// </summary>
public static class MesProtocolRegistry
{
    /// <summary>为给定协议创建对应的传输工厂实例。</summary>
    public static IMesTransportFactory CreateFactory(MesProtocolKind protocol) => protocol switch
    {
        MesProtocolKind.InMemory => new InMemoryTransportFactory(),
        MesProtocolKind.Rest => new RestTransportFactory(),
        MesProtocolKind.Mqtt => new MqttTransportFactory(),
        MesProtocolKind.OpcUa => new OpcUaTransportFactory(),
        MesProtocolKind.FileDrop => new FileDropTransportFactory(),
        MesProtocolKind.Database => new DatabaseTransportFactory(),
        MesProtocolKind.SecsGem => new SecsGemTransportFactory(),
        MesProtocolKind.Soap => new SoapTransportFactory(),
        MesProtocolKind.WebSocket => new WebSocketTransportFactory(),
        MesProtocolKind.Kafka => new KafkaTransportFactory(),
        MesProtocolKind.Amqp => new AmqpTransportFactory(),
        MesProtocolKind.Grpc => new GrpcTransportFactory(),
        _ => throw new Core.Exceptions.MesConfigurationException($"未知或不支持的协议：{protocol}。")
    };

    /// <summary>返回全部内置协议的工厂（用于一次性注册所有 Provider）。</summary>
    public static IEnumerable<IMesTransportFactory> CreateAllFactories()
    {
        yield return new InMemoryTransportFactory();
        yield return new RestTransportFactory();
        yield return new MqttTransportFactory();
        yield return new OpcUaTransportFactory();
        yield return new FileDropTransportFactory();
        yield return new DatabaseTransportFactory();
        yield return new SecsGemTransportFactory();
        yield return new SoapTransportFactory();
        yield return new WebSocketTransportFactory();
        yield return new KafkaTransportFactory();
        yield return new AmqpTransportFactory();
        yield return new GrpcTransportFactory();
    }

    /// <summary>向 DI 容器注册全部内置协议的传输工厂。</summary>
    public static IServiceCollection AddAllProtocols(this IServiceCollection services)
    {
        foreach (var factory in CreateAllFactories())
            services.AddSingleton(factory);
        return services;
    }
}
