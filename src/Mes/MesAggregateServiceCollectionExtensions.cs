using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.DependencyInjection;
using Mes.Core.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mes;

/// <summary>
/// 聚合包的依赖注入扩展：一次注册核心服务 + 匹配协议的 Provider + 默认客户端，
/// 让接入方“零代码切协议”。
/// </summary>
public static class MesAggregateServiceCollectionExtensions
{
    /// <summary>
    /// 用一行连接字符串注册默认 <see cref="IMesClient"/>（自动选择并注册对应 Provider）。
    /// </summary>
    /// <param name="services">服务容器。</param>
    /// <param name="connectionString">形如 <c>kafka://broker:9092?prefix=mes</c> 的连接字符串。</param>
    /// <param name="configure">可选后置配置。</param>
    public static IServiceCollection AddMes(this IServiceCollection services, string connectionString, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new MesOptions();
        var protocol = MesConnectionString.Apply(options, connectionString);
        configure?.Invoke(options);
        return RegisterClient(services, options, protocol);
    }

    /// <summary>
    /// 从配置注册默认 <see cref="IMesClient"/>。读取 <paramref name="sectionName"/> 配置节：
    /// 支持 <c>ConnectionString</c> 一行式，或分字段（<c>Protocol</c>、<c>Endpoint</c>、<c>Auth</c>、<c>Properties</c> 等）。
    /// </summary>
    /// <param name="services">服务容器。</param>
    /// <param name="configuration">配置源（通常来自 appsettings.json）。</param>
    /// <param name="sectionName">配置节名称，默认 <c>Mes</c>。</param>
    /// <param name="configure">可选后置配置。</param>
    public static IServiceCollection AddMes(this IServiceCollection services, IConfiguration configuration, string sectionName = "Mes", Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(sectionName);
        var options = new MesOptions();

        var connectionString = section["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            MesConnectionString.Apply(options, connectionString);

        // 分字段绑定可覆盖/补充连接字符串的结果。
        section.Bind(options);
        configure?.Invoke(options);

        return RegisterClient(services, options, options.Protocol);
    }

    /// <summary>
    /// 用委托配置注册默认 <see cref="IMesClient"/>：需在委托内设置 <see cref="MesOptions.Protocol"/>，
    /// 由聚合包按协议自动挂接对应 Provider。
    /// </summary>
    public static IServiceCollection AddMes(this IServiceCollection services, Action<MesOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new MesOptions();
        configure(options);
        return RegisterClient(services, options, options.Protocol);
    }

    private static IServiceCollection RegisterClient(IServiceCollection services, MesOptions options, MesProtocolKind protocol)
    {
        services.AddMesCore();
        services.AddSingleton(options);
        services.AddSingleton<IMesTransportFactory>(MesProtocolRegistry.CreateFactory(protocol));
        services.TryAddSingleton<IMesClient>(sp =>
        {
            var factory = sp.GetRequiredService<IMesClientFactory>();
            return factory.Create(options);
        });
        return services;
    }
}
