using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Rest;

/// <summary>
/// REST 传输工厂。
/// </summary>
public sealed class RestTransportFactory : IMesTransportFactory
{
    private readonly Func<MesOptions, HttpClient?>? _httpClientProvider;

    /// <summary>构造。可选地提供 <see cref="HttpClient"/> 工厂（用于共享/测试）。</summary>
    public RestTransportFactory(Func<MesOptions, HttpClient?>? httpClientProvider = null)
        => _httpClientProvider = httpClientProvider;

    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.Rest;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new RestTransport(options, _httpClientProvider?.Invoke(options), loggerFactory.CreateLogger<RestTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options) => MesOperationCatalog.CreateRestDefaults();
}

/// <summary>
/// REST Provider 注册扩展。
/// </summary>
public static class RestProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用 REST 协议。</summary>
    public static MesClientBuilder UseRest(this MesClientBuilder builder, string baseAddress, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTransportFactory(new RestTransportFactory());
        builder.Options.Protocol = MesProtocolKind.Rest;
        builder.Options.Endpoint.BaseAddress = baseAddress;
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册 REST 传输工厂。</summary>
    public static IServiceCollection AddRestProtocol(this IServiceCollection services, Func<MesOptions, HttpClient?>? httpClientProvider = null)
    {
        services.AddSingleton<IMesTransportFactory>(new RestTransportFactory(httpClientProvider));
        return services;
    }
}
