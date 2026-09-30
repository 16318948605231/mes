using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Soap;

/// <summary>
/// SOAP 传输工厂。
/// </summary>
public sealed class SoapTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.Soap;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new SoapTransport(options, logger: loggerFactory.CreateLogger<SoapTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options)
        => MesOperationCatalog.CreateSoapDefaults();
}

/// <summary>
/// SOAP Provider 注册扩展。
/// </summary>
public static class SoapProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用 SOAP 协议。</summary>
    /// <param name="builder">构建器。</param>
    /// <param name="serviceUrl">SOAP 服务端点 URL。</param>
    /// <param name="serviceNamespace">服务命名空间（默认 <c>http://mes.local/</c>）。</param>
    /// <param name="configure">可选的附加配置。</param>
    public static MesClientBuilder UseSoap(this MesClientBuilder builder, string serviceUrl, string? serviceNamespace = null, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);
        builder.AddTransportFactory(new SoapTransportFactory());
        builder.Options.Protocol = MesProtocolKind.Soap;
        builder.Options.Endpoint.BaseAddress = serviceUrl;
        if (!string.IsNullOrWhiteSpace(serviceNamespace))
            builder.Options.SetProperty("Namespace", serviceNamespace);
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册 SOAP 传输工厂。</summary>
    public static IServiceCollection AddSoapProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new SoapTransportFactory());
        return services;
    }
}
