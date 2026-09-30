using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Database;

/// <summary>
/// 数据库传输工厂。
/// </summary>
public sealed class DatabaseTransportFactory : IMesTransportFactory
{
    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.Database;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new DatabaseTransport(options, loggerFactory.CreateLogger<DatabaseTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options) => CreateDefaults();

    /// <summary>
    /// 创建示例 SQL 默认目录（配合内置示例架构 mes_*）。生产环境应按实际库表覆盖。
    /// </summary>
    public static MesOperationCatalog CreateDefaults()
    {
        var c = new MesOperationCatalog();
        c.Map(MesOperationKeys.GetWorkOrder, "SELECT data FROM mes_workorder WHERE id=@workOrderId", "SELECT");
        c.Map(MesOperationKeys.GetUnit, "SELECT data FROM mes_unit WHERE sn=@serialNumber", "SELECT");
        c.Map(MesOperationKeys.GetRecipe, "SELECT data FROM mes_recipe WHERE id=@recipeId", "SELECT");
        c.Map(MesOperationKeys.GetTraceability, "SELECT data FROM mes_traceability WHERE sn=@serialNumber", "SELECT");
        c.Map(MesOperationKeys.CheckUnitPassed, "SELECT data FROM mes_gate WHERE sn=@serialNumber AND op=@operationId", "SELECT");
        c.Map(MesOperationKeys.ReportInspection, "INSERT INTO mes_inspection(sn,data,created_at) VALUES(@serialNumber,@json,@now)", "INSERT");
        c.Map(MesOperationKeys.ReportMeasurements, "INSERT INTO mes_measurement(sn,data,created_at) VALUES(@serialNumber,@json,@now)", "INSERT");
        c.Map(MesOperationKeys.ReportDeviceStatus, "INSERT INTO mes_device_status(device_id,data,created_at) VALUES(@deviceId,@json,@now)", "INSERT");
        c.Map(MesOperationKeys.RaiseAlarm, "INSERT INTO mes_alarm(code,data,created_at) VALUES(@alarmCode,@json,@now)", "INSERT");
        c.Map(MesOperationKeys.ClearAlarm, "INSERT INTO mes_alarm(code,data,created_at) VALUES(@alarmCode,@json,@now)", "INSERT");
        c.Map(MesOperationKeys.UploadImage, "INSERT INTO mes_image(id,data,created_at) VALUES(@imageId,@json,@now)", "INSERT", requestResponse: false);
        return c;
    }
}

/// <summary>
/// 数据库 Provider 注册扩展。
/// </summary>
public static class DatabaseProtocolExtensions
{
    /// <summary>为 Fluent 构建器启用数据库协议。</summary>
    public static MesClientBuilder UseDatabase(this MesClientBuilder builder, string connectionString, string provider = "Sqlite", Action<MesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTransportFactory(new DatabaseTransportFactory());
        builder.Options.Protocol = MesProtocolKind.Database;
        builder.Options.Endpoint.BaseAddress = connectionString;
        builder.Options.SetProperty("Provider", provider);
        configure?.Invoke(builder.Options);
        return builder;
    }

    /// <summary>向 DI 容器注册数据库传输工厂。</summary>
    public static IServiceCollection AddDatabaseProtocol(this IServiceCollection services)
    {
        services.AddSingleton<IMesTransportFactory>(new DatabaseTransportFactory());
        return services;
    }
}
