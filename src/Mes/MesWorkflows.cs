using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Workflow;
using Microsoft.Extensions.Configuration;

namespace Mes;

/// <summary>
/// 极简入口：无需 DI 容器，直接从配置/连接字符串创建配置驱动的 <see cref="IMesWorkflowHost"/>。
/// 检测软件典型用法：启动时 <c>var mes = MesWorkflows.Create(configuration);</c>，
/// 之后在检测前/中/后各 <c>await mes.RunAsync("BeforeInspection"/…, ctx);</c> 一次即可。
/// </summary>
public static class MesWorkflows
{
    /// <summary>
    /// 从 <see cref="IConfiguration"/> 创建工作流宿主：读取 <paramref name="sectionName"/> 节，
    /// 支持 <c>ConnectionString</c> 一行式或分字段配置，并绑定 <c>Enabled</c> 与 <c>Workflows</c>。
    /// </summary>
    /// <param name="configuration">配置源（通常来自 appsettings.json）。</param>
    /// <param name="sectionName">配置节名称，默认 <c>Mes</c>。</param>
    public static IMesWorkflowHost Create(IConfiguration configuration, string sectionName = "Mes")
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = MesClientBuilder.Create();
        var section = configuration.GetSection(sectionName);

        var connectionString = section["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            MesConnectionString.Apply(builder.Options, connectionString);

        // 分字段绑定可覆盖/补充连接字符串的结果（含 Enabled / Workflows / OperationBindings）。
        section.Bind(builder.Options);

        builder.AddTransportFactory(MesProtocolRegistry.CreateFactory(builder.Options.Protocol));
        var client = builder.Build();
        return new MesWorkflowHost(client, builder.Options);
    }

    /// <summary>
    /// 用一行连接字符串创建工作流宿主（阶段步骤通常仍从配置绑定；此重载便于代码内直连后用
    /// <paramref name="configure"/> 填充 <c>Workflows</c>）。
    /// </summary>
    /// <param name="connectionString">形如 <c>rest://api.example.com?token=abc</c> 的连接字符串。</param>
    /// <param name="configure">可选后置配置（设置 <c>Enabled</c>、<c>Workflows</c> 等）。</param>
    public static IMesWorkflowHost Create(string connectionString, Action<MesOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = MesClients.CreateBuilder(connectionString, configure);
        var client = builder.Build();
        return new MesWorkflowHost(client, builder.Options);
    }

    /// <summary>用已创建的 <see cref="IMesClient"/> 与 <see cref="MesOptions"/> 包装一个工作流宿主。</summary>
    public static IMesWorkflowHost Create(IMesClient client, MesOptions options)
        => new MesWorkflowHost(client, options);
}
