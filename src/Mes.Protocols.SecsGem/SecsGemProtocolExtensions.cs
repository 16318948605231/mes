using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.SecsGem;

/// <summary>
/// SECS/GEM 传输工厂。
/// </summary>
public sealed class SecsGemTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.SecsGem;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new SecsGemTransport(options, loggerFactory);

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options) => CreateDefaults();

    /// <summary>
    /// 创建一套默认 SxFy 映射。<b>注意：</b>这些是可覆盖的占位映射，实际 Stream/Function 及数据项
    /// 结构必须与目标主机的 GEM 规范一致，请通过 <c>OperationBindings</c> 覆盖。查询类映射到
    /// 奇数一级功能（期望回复），上报/事件类映射到 S6/S5。
    /// </summary>
    public static MesOperationCatalog CreateDefaults()
    {
        var c = new MesOperationCatalog();
        c.Map(MesOperationKeys.GetWorkOrder, "S1F3", "query");
        c.Map(MesOperationKeys.GetUnit, "S1F5", "query");
        c.Map(MesOperationKeys.GetRecipe, "S1F13", "query");
        c.Map(MesOperationKeys.GetTraceability, "S1F15", "query");
        c.Map(MesOperationKeys.CheckUnitPassed, "S1F17", "query");
        c.Map(MesOperationKeys.ReportInspection, "S6F11", "report");
        c.Map(MesOperationKeys.ReportMeasurements, "S6F13", "report");
        c.Map(MesOperationKeys.ReportDeviceStatus, "S6F15", "report");
        c.Map(MesOperationKeys.RaiseAlarm, "S5F1", "report");
        c.Map(MesOperationKeys.ClearAlarm, "S5F3", "report");
        c.Map(MesOperationKeys.UploadImage, "S6F17", "report");
        return c;
    }
}

/// <summary>
/// SECS/GEM Provider 注册扩展。
/// </summary>
public static class SecsGemProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用 SECS/GEM 协议。</summary>
    public static MesClientBuilder UseSecsGem(this MesClientBuilder builder, string host, int port = 5000, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTransportFactory(new SecsGemTransportFactory());
        builder.Options.Protocol = MesProtocolKind.SecsGem;
        builder.Options.Endpoint.Host = host;
        builder.Options.Endpoint.Port = port;
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册 SECS/GEM 传输工厂。</summary>
    public static IServiceCollection AddSecsGemProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new SecsGemTransportFactory());
        return services;
    }
}
