using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Grpc;

/// <summary>
/// gRPC 传输工厂。
/// </summary>
public sealed class GrpcTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.Grpc;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new GrpcTransport(options, loggerFactory.CreateLogger<GrpcTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options)
        => MesOperationCatalog.CreateGrpcDefaults();
}

/// <summary>
/// gRPC Provider 注册扩展。
/// </summary>
public static class GrpcProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用 gRPC 协议。</summary>
    public static MesClientBuilder UseGrpc(this MesClientBuilder builder, string address, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        builder.AddTransportFactory(new GrpcTransportFactory());
        builder.Options.Protocol = MesProtocolKind.Grpc;
        builder.Options.Endpoint.BaseAddress = address;
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册 gRPC 传输工厂。</summary>
    public static IServiceCollection AddGrpcProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new GrpcTransportFactory());
        return services;
    }
}
