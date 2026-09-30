using Mes.Core.Builder;
using Mes.Core.Client;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 面向真实 Broker 的集成测试。默认无 Broker 环境变量时直接跳过（早退），
/// 便于本地/CI 在提供环境变量后启用：
/// <list type="bullet">
/// <item><c>MES_KAFKA_BOOTSTRAP</c>（如 <c>localhost:9092</c>）</item>
/// <item><c>MES_AMQP_URI</c>（如 <c>amqp://localhost:5672</c>）</item>
/// <item><c>MES_GRPC_URL</c>（如 <c>grpc://localhost:5000</c>）</item>
/// </list>
/// </summary>
public sealed class BrokerIntegrationTests
{
    [Fact]
    public async Task Kafka_Connect_WhenBrokerConfigured()
    {
        var bootstrap = Environment.GetEnvironmentVariable("MES_KAFKA_BOOTSTRAP");
        if (string.IsNullOrWhiteSpace(bootstrap))
            return; // 未配置 Broker，跳过集成验证。

        await using var client = global::Mes.MesClients.Connect($"kafka://{bootstrap}");
        var ok = await client.TestConnectionAsync();
        Assert.True(ok.Success, ok.Message);
    }

    [Fact]
    public async Task Amqp_Connect_WhenBrokerConfigured()
    {
        var uri = Environment.GetEnvironmentVariable("MES_AMQP_URI");
        if (string.IsNullOrWhiteSpace(uri))
            return;

        await using var client = global::Mes.MesClients.Connect(uri);
        var ok = await client.TestConnectionAsync();
        Assert.True(ok.Success, ok.Message);
    }

    [Fact]
    public async Task Grpc_Connect_WhenGatewayConfigured()
    {
        var url = Environment.GetEnvironmentVariable("MES_GRPC_URL");
        if (string.IsNullOrWhiteSpace(url))
            return;

        await using var client = global::Mes.MesClients.Connect(url);
        var ok = await client.TestConnectionAsync();
        Assert.True(ok.Success, ok.Message);
    }
}
