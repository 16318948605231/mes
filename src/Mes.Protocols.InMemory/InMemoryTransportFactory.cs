using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Serialization;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.InMemory;

/// <summary>
/// 进程内回环传输工厂。
/// </summary>
public sealed class InMemoryTransportFactory : IMesTransportFactory
{
    private readonly InMemoryMesServer _server;
    private readonly IMesSerializer _serializer;

    /// <summary>使用指定服务器构造（多个客户端可共享同一服务器）。</summary>
    public InMemoryTransportFactory(InMemoryMesServer? server = null, IMesSerializer? serializer = null)
    {
        _serializer = serializer ?? new JsonMesSerializer();
        _server = server ?? new InMemoryMesServer(_serializer);
    }

    /// <summary>底层内存服务器（用于预置数据/断言）。</summary>
    public InMemoryMesServer Server => _server;

    /// <inheritdoc />
    public MesProtocolKind Protocol => MesProtocolKind.InMemory;

    /// <inheritdoc />
    public IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory)
        => new InMemoryTransport(_server, _serializer, options.Name, loggerFactory.CreateLogger<InMemoryTransport>());

    /// <inheritdoc />
    public MesOperationCatalog CreateDefaultCatalog(MesOptions options) => CreateDefaults();

    /// <summary>创建以操作键作为通道名的默认目录（回环无需 URL/主题）。</summary>
    public static MesOperationCatalog CreateDefaults()
    {
        var c = new MesOperationCatalog();
        string[] keys =
        [
            MesOperationKeys.GetWorkOrder, MesOperationKeys.GetUnit, MesOperationKeys.GetRecipe,
            MesOperationKeys.GetTraceability, MesOperationKeys.CheckUnitPassed,
            MesOperationKeys.ReportInspection, MesOperationKeys.ReportMeasurements, MesOperationKeys.UploadImage,
            MesOperationKeys.ReportDeviceStatus, MesOperationKeys.RaiseAlarm, MesOperationKeys.ClearAlarm
        ];
        foreach (var k in keys)
            c.Map(k, k, verb: "POST", requestResponse: true);
        return c;
    }
}
