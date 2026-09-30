using Mes.Core.Client;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 聚合包（<c>Mes</c>）与协议注册表测试：验证“一行连接字符串创建客户端”“协议→工厂映射”。
/// </summary>
public sealed class AggregatePackageTests
{
    [Fact]
    public async Task Connect_WithConnectionString_CreatesWorkingClient()
    {
        await using var client = global::Mes.MesClients.Connect("mem://local");
        Assert.NotNull(client);
        Assert.True(await client.PingAsync());
    }

    [Theory]
    [InlineData(MesProtocolKind.InMemory)]
    [InlineData(MesProtocolKind.Rest)]
    [InlineData(MesProtocolKind.Mqtt)]
    [InlineData(MesProtocolKind.OpcUa)]
    [InlineData(MesProtocolKind.FileDrop)]
    [InlineData(MesProtocolKind.Database)]
    [InlineData(MesProtocolKind.SecsGem)]
    [InlineData(MesProtocolKind.Soap)]
    [InlineData(MesProtocolKind.WebSocket)]
    [InlineData(MesProtocolKind.Kafka)]
    [InlineData(MesProtocolKind.Amqp)]
    [InlineData(MesProtocolKind.Grpc)]
    public void Registry_CreateFactory_ReturnsMatchingProtocol(MesProtocolKind kind)
    {
        var factory = global::Mes.MesProtocolRegistry.CreateFactory(kind);
        Assert.Equal(kind, factory.Protocol);
    }

    [Fact]
    public void Registry_CreateAllFactories_CoversTwelveProtocols()
    {
        var kinds = global::Mes.MesProtocolRegistry.CreateAllFactories()
            .Select(f => f.Protocol)
            .Distinct()
            .ToList();
        Assert.Equal(12, kinds.Count);
    }
}

/// <summary>
/// 各新协议默认操作目录测试：确保标准业务操作已注册，接入方无需手工映射即可调用。
/// </summary>
public sealed class ProtocolCatalogTests
{
    public static IEnumerable<object[]> Catalogs()
    {
        yield return new object[] { MesOperationCatalog.CreateSoapDefaults() };
        yield return new object[] { MesOperationCatalog.CreateKafkaDefaults() };
        yield return new object[] { MesOperationCatalog.CreateAmqpDefaults() };
        yield return new object[] { MesOperationCatalog.CreateWebSocketDefaults() };
        yield return new object[] { MesOperationCatalog.CreateGrpcDefaults() };
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void Catalog_HasCoreOperations(MesOperationCatalog catalog)
    {
        Assert.True(catalog.Count > 0);
        Assert.True(catalog.Contains(MesOperationKeys.GetWorkOrder));
        Assert.True(catalog.Contains(MesOperationKeys.ReportInspection));
    }
}
