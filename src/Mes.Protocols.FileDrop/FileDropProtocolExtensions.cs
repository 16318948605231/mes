using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.FileDrop;

/// <summary>
/// 文件落地传输工厂。
/// </summary>
public sealed class FileDropTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.FileDrop;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new FileDropTransport(options, loggerFactory.CreateLogger<FileDropTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options) => CreateDefaults();

    /// <summary>创建文件落地默认目录：上报=写目录；查询=读文件。</summary>
    public static MesOperationCatalog CreateDefaults()
    {
        var c = new MesOperationCatalog();
        // 上报（发布语义，写入 outbox 目录）
        c.Map(MesOperationKeys.ReportInspection, "outbox/inspection", requestResponse: false);
        c.Map(MesOperationKeys.ReportMeasurements, "outbox/measurements", requestResponse: false);
        c.Map(MesOperationKeys.ReportDeviceStatus, "outbox/device-status", requestResponse: false);
        c.Map(MesOperationKeys.RaiseAlarm, "outbox/alarms", requestResponse: false);
        c.Map(MesOperationKeys.ClearAlarm, "outbox/alarms-clear", requestResponse: false);
        c.Map(MesOperationKeys.UploadImage, "outbox/images", requestResponse: false);
        // 查询（请求语义，读取 inbox 文件）
        c.Map(MesOperationKeys.GetWorkOrder, "inbox/workorders/{workOrderId}", "GET");
        c.Map(MesOperationKeys.GetUnit, "inbox/units/{serialNumber}", "GET");
        c.Map(MesOperationKeys.GetRecipe, "inbox/recipes/{recipeId}", "GET");
        c.Map(MesOperationKeys.GetTraceability, "inbox/traceability/{serialNumber}", "GET");
        c.Map(MesOperationKeys.CheckUnitPassed, "inbox/gate/{serialNumber}_{operationId}", "GET");
        return c;
    }
}

/// <summary>
/// 文件落地 Provider 注册扩展。
/// </summary>
public static class FileDropProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用文件落地协议。</summary>
    public static MesClientBuilder UseFileDrop(this MesClientBuilder builder, string rootDirectory, Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTransportFactory(new FileDropTransportFactory());
        builder.Options.Protocol = MesProtocolKind.FileDrop;
        builder.Options.Endpoint.BaseAddress = rootDirectory;
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册文件落地传输工厂。</summary>
    public static IServiceCollection AddFileDropProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new FileDropTransportFactory());
        return services;
    }
}
