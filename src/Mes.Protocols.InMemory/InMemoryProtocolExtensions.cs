using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mes.Protocols.InMemory;

/// <summary>
/// 进程内回环 Provider 的注册扩展。
/// </summary>
public static class InMemoryProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用进程内回环协议。</summary>
    public static MesClientBuilder UseInMemory(this MesClientBuilder builder, InMemoryMesServer? server = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var factory = new InMemoryTransportFactory(server);
        builder.AddTransportFactory(factory);
        builder.Options.Protocol = MesProtocolKind.InMemory;
        return builder;
    }

    /// <summary>向 DI 容器注册进程内回环传输工厂与服务器。</summary>
    public static IServiceCollection AddInMemoryProtocol(this IServiceCollection services, InMemoryMesServer? server = null)
    {
        var srv = server ?? new InMemoryMesServer();
        services.TryAddSingleton(srv);
        services.AddSingleton<IMesTransportFactory>(sp => new InMemoryTransportFactory(sp.GetService<InMemoryMesServer>()));
        return services;
    }
}
