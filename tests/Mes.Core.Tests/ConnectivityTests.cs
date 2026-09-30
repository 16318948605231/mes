using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Enums;
using Mes.Protocols.InMemory;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 连通性自检（<c>TestConnectionAsync</c>/<c>PingAsync</c>）测试，使用进程内回环。
/// </summary>
public sealed class ConnectivityTests
{
    private static IMesClient CreateClient()
        => MesClientBuilder.Create().UseInMemory(new InMemoryMesServer()).Build();

    [Fact]
    public async Task TestConnection_Succeeds_OverInMemory()
    {
        var client = CreateClient();
        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal(MesConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task Ping_ReturnsTrue_OverInMemory()
    {
        var client = CreateClient();
        Assert.True(await client.PingAsync());
    }
}
