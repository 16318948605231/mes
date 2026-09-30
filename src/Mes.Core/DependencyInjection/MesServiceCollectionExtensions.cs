using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mes.Core.DependencyInjection;

/// <summary>
/// MES 依赖注入扩展。
/// </summary>
public static class MesServiceCollectionExtensions
{
    /// <summary>注册 MES 核心服务（序列化器、客户端工厂）。协议 Provider 需另行注册其传输工厂。</summary>
    public static IServiceCollection AddMesCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IMesSerializer, JsonMesSerializer>();
        services.TryAddSingleton<IMesClientFactory, MesClientFactory>();
        return services;
    }

    /// <summary>
    /// 注册一个具名 MES 配置，供 <see cref="IMesClientFactory.Create(string)"/> 使用。
    /// </summary>
    public static IServiceCollection AddMesOptions(this IServiceCollection services, Action<MesOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddMesCore();
        var options = new MesOptions();
        configure(options);
        services.AddSingleton(options);
        return services;
    }

    /// <summary>
    /// 注册一个默认 <see cref="IMesClient"/> 单例（根据给定配置创建）。
    /// </summary>
    /// <remarks>
    /// 默认客户端绑定到<b>本次调用</b>提供的具体 <see cref="MesOptions"/> 实例（由闭包捕获），
    /// 因此不受容器中其它 <see cref="MesOptions"/> 注册顺序影响；多次调用时按 <c>TryAdd</c> 语义保留首个默认客户端，
    /// 其余具名配置仍可经 <see cref="IMesClientFactory.Create(string)"/> 解析。
    /// </remarks>
    public static IServiceCollection AddMesClient(this IServiceCollection services, Action<MesOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddMesCore();

        var options = new MesOptions();
        configure(options);
        services.AddSingleton(options);

        services.TryAddSingleton<IMesClient>(sp =>
        {
            var factory = sp.GetRequiredService<IMesClientFactory>();
            return factory.Create(options);
        });
        return services;
    }
}
