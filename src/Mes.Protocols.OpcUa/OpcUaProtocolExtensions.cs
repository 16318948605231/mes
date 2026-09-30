using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.OpcUa;

/// <summary>
/// OPC UA 传输工厂。
/// </summary>
public sealed class OpcUaTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.OpcUa;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new OpcUaTransport(options, loggerFactory);

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options) => CreateDefaults();

    /// <summary>
    /// 创建一套默认地址空间映射（<c>ns=2</c>、字符串标识）。生产环境按实际服务器地址空间覆盖。
    /// 读取类操作映射为节点 Value 读取；上报类操作映射为方法调用。
    /// </summary>
    public static MesOperationCatalog CreateDefaults()
    {
        var c = new MesOperationCatalog();
        c.Map(MesOperationKeys.GetWorkOrder, "ns=2;s=MES/WorkOrder/{workOrderId}", "read");
        c.Map(MesOperationKeys.GetUnit, "ns=2;s=MES/Unit/{serialNumber}", "read");
        c.Map(MesOperationKeys.GetRecipe, "ns=2;s=MES/Recipe/{recipeId}", "read");
        c.Map(MesOperationKeys.GetTraceability, "ns=2;s=MES/Traceability/{serialNumber}", "read");
        c.Map(MesOperationKeys.CheckUnitPassed, "ns=2;s=MES/Gate/{serialNumber}/{operationId}", "read");
        c.Map(MesOperationKeys.ReportInspection, "ns=2;s=MES|ns=2;s=MES/ReportInspection", "call");
        c.Map(MesOperationKeys.ReportMeasurements, "ns=2;s=MES|ns=2;s=MES/ReportMeasurements", "call");
        c.Map(MesOperationKeys.ReportDeviceStatus, "ns=2;s=MES|ns=2;s=MES/ReportDeviceStatus", "call");
        c.Map(MesOperationKeys.RaiseAlarm, "ns=2;s=MES|ns=2;s=MES/RaiseAlarm", "call");
        c.Map(MesOperationKeys.ClearAlarm, "ns=2;s=MES|ns=2;s=MES/ClearAlarm", "call");
        c.Map(MesOperationKeys.UploadImage, "ns=2;s=MES|ns=2;s=MES/UploadImage", "call");
        return c;
    }
}

/// <summary>
/// OPC UA Provider 注册扩展。
/// </summary>
public static class OpcUaProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用 OPC UA 协议。</summary>
    public static MesClientBuilder UseOpcUa(this MesClientBuilder builder, string endpointUrl, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTransportFactory(new OpcUaTransportFactory());
        builder.Options.Protocol = MesProtocolKind.OpcUa;
        builder.Options.Endpoint.BaseAddress = endpointUrl;
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册 OPC UA 传输工厂。</summary>
    public static IServiceCollection AddOpcUaProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new OpcUaTransportFactory());
        return services;
    }
}
