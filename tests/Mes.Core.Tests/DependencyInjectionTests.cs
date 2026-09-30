using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.DependencyInjection;
using Mes.Core.Enums;
using Mes.Protocols.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 验证依赖注入扩展：默认 <see cref="IMesClient"/> 绑定到 <c>AddMesClient</c> 调用时提供的具体配置实例，
/// 不受随后注册的其它 <see cref="MesOptions"/> 影响（回归此前使用 <c>.Last()</c> 导致的顺序耦合缺陷）。
/// </summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task AddMesClient_BindsDefaultClient_ToItsOwnOptions_NotLastRegistered()
    {
        var services = new ServiceCollection();
        services.AddInMemoryProtocol();
        // 默认客户端应绑定到名为 "primary" 的配置。
        services.AddMesClient(o =>
        {
            o.Name = "primary";
            o.Protocol = MesProtocolKind.InMemory;
        });
        // 之后再注册另一份具名配置；旧实现会因 .Last() 而错误地采用它。
        services.AddMesOptions(o =>
        {
            o.Name = "secondary";
            o.Protocol = MesProtocolKind.InMemory;
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMesClient>();

        Assert.Equal("primary", client.Name);
    }

    [Fact]
    public async Task AddMesClient_CalledTwice_KeepsFirstDefault_AndResolvesBothByName()
    {
        var services = new ServiceCollection();
        services.AddInMemoryProtocol();
        services.AddMesClient(o =>
        {
            o.Name = "alpha";
            o.Protocol = MesProtocolKind.InMemory;
        });
        services.AddMesClient(o =>
        {
            o.Name = "beta";
            o.Protocol = MesProtocolKind.InMemory;
        });

        await using var provider = services.BuildServiceProvider();

        // TryAdd 语义：默认客户端保持首个注册（alpha）。
        var defaultClient = provider.GetRequiredService<IMesClient>();
        Assert.Equal("alpha", defaultClient.Name);

        // 两份配置仍可经工厂按名解析。
        var factory = provider.GetRequiredService<IMesClientFactory>();
        Assert.Equal("alpha", factory.Create("alpha").Name);
        Assert.Equal("beta", factory.Create("beta").Name);
    }
}
