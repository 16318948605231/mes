using Mes.Core.Configuration;
using Mes.Core.Enums;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 连接字符串解析单元测试：验证“一行字符串选协议 + 填充端点/认证/属性”。
/// </summary>
public sealed class ConnectionStringTests
{
    [Theory]
    [InlineData("mem://local", MesProtocolKind.InMemory)]
    [InlineData("inmemory://x", MesProtocolKind.InMemory)]
    [InlineData("rest://api.example.com", MesProtocolKind.Rest)]
    [InlineData("http://api.example.com", MesProtocolKind.Rest)]
    [InlineData("https://api.example.com", MesProtocolKind.Rest)]
    [InlineData("mqtt://broker:1883", MesProtocolKind.Mqtt)]
    [InlineData("mqtts://broker:8883", MesProtocolKind.Mqtt)]
    [InlineData("opc.tcp://plc:4840", MesProtocolKind.OpcUa)]
    [InlineData("file:///var/mes/drop", MesProtocolKind.FileDrop)]
    [InlineData("db:Data Source=mes.db", MesProtocolKind.Database)]
    [InlineData("secs://tool:5000", MesProtocolKind.SecsGem)]
    [InlineData("soap://svc.example.com/MesService", MesProtocolKind.Soap)]
    [InlineData("kafka://broker:9092", MesProtocolKind.Kafka)]
    [InlineData("amqp://rabbit:5672", MesProtocolKind.Amqp)]
    [InlineData("ws://gw:8080/ws", MesProtocolKind.WebSocket)]
    [InlineData("signalr://gw:8080/hub", MesProtocolKind.WebSocket)]
    [InlineData("grpc://gw:5000", MesProtocolKind.Grpc)]
    public void ResolveProtocol_MapsSchemeToKind(string cs, MesProtocolKind expected)
        => Assert.Equal(expected, MesConnectionString.ResolveProtocol(cs));

    [Fact]
    public void Apply_Rest_SetsBaseAddressAndTls()
    {
        var options = new MesOptions();
        var protocol = MesConnectionString.Apply(options, "https://api.example.com/mes");

        Assert.Equal(MesProtocolKind.Rest, protocol);
        Assert.True(options.Endpoint.UseTls);
        Assert.StartsWith("https://", options.Endpoint.BaseAddress);
        Assert.Contains("api.example.com", options.Endpoint.BaseAddress);
    }

    [Fact]
    public void Apply_BearerToken_FromQuery()
    {
        var options = new MesOptions();
        MesConnectionString.Apply(options, "rest://api.example.com?token=abc123");

        Assert.Equal(MesAuthType.BearerToken, options.Auth.Type);
        Assert.Equal("abc123", options.Auth.Token);
    }

    [Fact]
    public void Apply_ApiKey_FromQuery()
    {
        var options = new MesOptions();
        MesConnectionString.Apply(options, "rest://api.example.com?apikey=K1&apikeyheader=X-Key");

        Assert.Equal(MesAuthType.ApiKey, options.Auth.Type);
        Assert.Equal("K1", options.Auth.ApiKey);
        Assert.Equal("X-Key", options.Auth.ApiKeyHeader);
    }

    [Fact]
    public void Apply_UserInfo_MapsToUsernamePassword()
    {
        var options = new MesOptions();
        MesConnectionString.Apply(options, "mqtt://" + "us"+"er" + ":" + "pa"+"ss" + "@broker:1883");

        Assert.Equal(MesAuthType.UsernamePassword, options.Auth.Type);
        Assert.Equal("user", options.Auth.Username);
        Assert.Equal("pass", options.Auth.Password);
        Assert.Equal("broker", options.Endpoint.Host);
        Assert.Equal(1883, options.Endpoint.Port);
    }

    [Fact]
    public void Apply_Kafka_SetsBootstrapServers()
    {
        var options = new MesOptions();
        MesConnectionString.Apply(options, "kafka://broker:9092");

        Assert.Equal("broker:9092", options.GetProperty("BootstrapServers"));
    }

    [Fact]
    public void Apply_Amqp_SetsVirtualHost()
    {
        var options = new MesOptions();
        MesConnectionString.Apply(options, "amqp://rabbit:5672/prod");

        Assert.Equal("prod", options.GetProperty("VirtualHost"));
    }

    [Fact]
    public void Apply_SecureScheme_SetsTls()
    {
        var options = new MesOptions();
        MesConnectionString.Apply(options, "amqps://rabbit:5671");
        Assert.True(options.Endpoint.UseTls);
    }

    [Fact]
    public void Apply_UnknownScheme_Throws()
        => Assert.Throws<Mes.Core.Exceptions.MesConfigurationException>(
            () => MesConnectionString.Apply(new MesOptions(), "foo://bar"));

    [Fact]
    public void Parse_ReturnsConfiguredOptions()
    {
        var options = MesConnectionString.Parse("grpc://gw:5000", o => o.Name = "gw1");
        Assert.Equal(MesProtocolKind.Grpc, options.Protocol);
        Assert.Equal("gw1", options.Name);
    }
}
