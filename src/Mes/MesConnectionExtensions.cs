using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;

namespace Mes;

/// <summary>
/// 面向“极简接入”的连接字符串扩展：一行字符串即可选定协议并创建客户端。
/// </summary>
public static class MesConnectionExtensions
{
    /// <summary>
    /// 用一行连接字符串配置构建器：解析 scheme→协议、填充端点/认证/属性，并挂接匹配的 Provider 工厂。
    /// </summary>
    /// <param name="builder">Fluent 构建器。</param>
    /// <param name="connectionString">形如 <c>mqtt://host:1883?prefix=mes</c> 的连接字符串。</param>
    /// <param name="configure">可选后置配置（在解析之后执行，用于覆盖个别字段）。</param>
    public static MesClientBuilder UseConnectionString(this MesClientBuilder builder, string connectionString, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var protocol = MesConnectionString.Apply(builder.Options, connectionString);
        builder.AddTransportFactory(MesProtocolRegistry.CreateFactory(protocol));
        configure?.Invoke(builder.Options);
        return builder;
    }
}

/// <summary>
/// 极简入口：无需 DI 容器，用一行连接字符串直接创建 <see cref="IMesClient"/>。
/// </summary>
public static class MesClients
{
    /// <summary>用连接字符串创建并返回一个 <see cref="IMesClient"/>。</summary>
    /// <param name="connectionString">形如 <c>rest://api.example.com?token=abc</c> 的连接字符串。</param>
    /// <param name="configure">可选后置配置。</param>
    public static IMesClient Connect(string connectionString, Action<MesOptions>? configure = null)
        => CreateBuilder(connectionString, configure).Build();

    /// <summary>用连接字符串创建一个已挂接对应 Provider 的构建器（便于继续链式配置）。</summary>
    public static MesClientBuilder CreateBuilder(string connectionString, Action<MesOptions>? configure = null)
        => MesClientBuilder.Create().UseConnectionString(connectionString, configure);
}
