using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Text.Json;
using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Enums;
using Mes.Core.Models;
using Mes.Core.Operations;
using Mes.Core.Serialization;
using Mes.Protocols.Mqtt;
using MQTTnet;
using MQTTnet.Server;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 使用进程内 MQTT Broker 验证 MQTT Provider：
/// 覆盖请求/响应（RPC）查询、单向上报发布、服务器推送订阅，以及请求超时。
/// 一个“响应端”MQTT 客户端模拟 MES/网关：读取请求的 <c>ResponseTopic</c> 与
/// <c>CorrelationData</c>，把结果回发到该响应主题。
/// </summary>
public sealed class MqttProviderTests : IAsyncLifetime
{
    private readonly int _port = GetFreeTcpPort();
    private readonly MqttServerFactory _serverFactory = new();
    private readonly ConcurrentQueue<(string topic, byte[] body)> _received = new();

    private MqttServer _server = null!;
    private IMqttClient _responder = null!;
    private IMesClient _client = null!;

    public async Task InitializeAsync()
    {
        // 1. 启动进程内 Broker。
        var serverOptions = _serverFactory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(_port)
            .Build();
        _server = _serverFactory.CreateMqttServer(serverOptions);
        await _server.StartAsync();

        // 2. 连接“响应端”客户端并订阅请求/上报主题。
        _responder = new MqttClientFactory().CreateMqttClient();
        _responder.ApplicationMessageReceivedAsync += OnResponderMessageAsync;
        await _responder.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer("localhost", _port)
            .WithClientId("mes-test-responder")
            .Build());

        foreach (var filter in new[] { "mes/query/#", "mes/inspection/#", "mes/measurement/#", "mes/alarm/#", "mes/device/#", "mes/image/#" })
        {
            await _responder.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(filter))
                .Build());
        }

        // 3. 构建并连接被测 MES 客户端。
        _client = MesClientBuilder.Create()
            .UseMqtt("localhost", _port, o =>
            {
                o.SetProperty("ClientId", "mes-under-test");
                o.TimeoutMs = 5000;
            })
            .Build();
        await _client.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();

        if (_responder is not null)
        {
            _responder.ApplicationMessageReceivedAsync -= OnResponderMessageAsync;
            if (_responder.IsConnected)
                await _responder.DisconnectAsync();
            _responder.Dispose();
        }

        if (_server is not null)
        {
            await _server.StopAsync(_serverFactory.CreateMqttServerStopOptionsBuilder().Build());
            _server.Dispose();
        }
    }

    [Fact]
    public async Task GetWorkOrder_RoundTrips_ReturnsWorkOrder()
    {
        var result = await _client.GetWorkOrderAsync("WO-1001");

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal("WO-1001", result.Value!.Id);
    }

    [Fact]
    public async Task CheckUnitPassed_RoundTrips_ReturnsTrue()
    {
        var result = await _client.CheckUnitPassedAsync("SN-1", "OP-10");

        Assert.True(result.Success, result.Message);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task ReportInspection_Publishes_ReachesBroker()
    {
        var result = await _client.ReportInspectionResultAsync(new InspectionResult
        {
            SerialNumber = "SN-9",
            Outcome = InspectionOutcome.Pass
        });

        Assert.True(result.Success, result.Message);
        Assert.True(await WaitForReportAsync(t => t.Contains("inspection") && t.Contains("SN-9"), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Subscribe_ReceivesServerPush()
    {
        var tcs = new TaskCompletionSource<Alarm>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sub = await _client.SubscribeAlarmsAsync("mes/push/alarm", (alarm, _) =>
        {
            tcs.TrySetResult(alarm);
            return Task.CompletedTask;
        });

        await PublishFromServerAsync("mes/push/alarm", new Alarm { Code = "E-01", Text = "过热" });

        var received = await WithTimeoutAsync(tcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal("E-01", received.Code);
    }

    [Fact]
    public async Task Subscribe_WildcardFilter_ReceivesServerPush()
    {
        var tcs = new TaskCompletionSource<Alarm>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sub = await _client.SubscribeAsync<Alarm>("mes/push/alarms/#", (alarm, _) =>
        {
            tcs.TrySetResult(alarm);
            return Task.CompletedTask;
        });

        await PublishFromServerAsync("mes/push/alarms/line1/oven", new Alarm { Code = "E-02", Text = "断线" });

        var received = await WithTimeoutAsync(tcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal("E-02", received.Code);
    }

    [Fact]
    public async Task Request_WithoutResponder_TimesOut()
    {
        await using var timeoutClient = MesClientBuilder.Create()
            .UseMqtt("localhost", _port, o =>
            {
                o.SetProperty("ClientId", "mes-timeout");
                o.TimeoutMs = 800;
            })
            .MapOperation("NoReplyOp", "mes/query/noreply", requestResponse: true)
            .Build();
        await timeoutClient.ConnectAsync();

        var result = await timeoutClient.InvokeAsync<WorkOrder>("NoReplyOp");

        Assert.False(result.Success);
    }

    private async Task PublishFromServerAsync(string topic, object payload)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonMesSerializer.DefaultOptions);
        await _responder.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(body)
            .WithContentType("application/json")
            .Build());
    }

    private async Task OnResponderMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var body = e.ApplicationMessage.Payload.IsEmpty ? Array.Empty<byte>() : e.ApplicationMessage.Payload.ToArray();

        // 无响应主题 => 单向上报，记录以供断言。
        if (string.IsNullOrEmpty(e.ApplicationMessage.ResponseTopic))
        {
            _received.Enqueue((topic, body));
            return;
        }

        // 请求/响应：按主题构造回复；返回 null 表示不回复（用于超时用例）。
        var reply = BuildReply(topic);
        if (reply is null)
            return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(reply, JsonMesSerializer.DefaultOptions);
        await _responder.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(e.ApplicationMessage.ResponseTopic)
            .WithPayload(payload)
            .WithCorrelationData(e.ApplicationMessage.CorrelationData)
            .WithContentType("application/json")
            .Build());
    }

    private static object? BuildReply(string topic)
    {
        var lastSegment = topic.Split('/')[^1];
        if (topic.Contains("/query/workorder/"))
            return new WorkOrder { Id = lastSegment, Number = lastSegment, ProductCode = "P-1" };
        if (topic.Contains("/query/unit/"))
            return new ProductUnit { SerialNumber = lastSegment, Status = "Pass" };
        if (topic.Contains("/query/recipe/"))
            return new Recipe { Id = lastSegment, Name = "R-" + lastSegment };
        if (topic.Contains("/query/gate/"))
            return new GateCheckResult { Passed = true };
        return null;
    }

    private async Task<bool> WaitForReportAsync(Func<string, bool> predicate, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (_received.Any(x => predicate(x.topic)))
                return true;
            await Task.Delay(50);
        }
        return false;
    }

    private static async Task<T> WithTimeoutAsync<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
            throw new TimeoutException("等待消息超时。");
        return await task;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}

/// <summary>
/// 验证 MQTT 默认操作目录：查询类映射为请求/响应（RPC），上报类映射为单向发布，
/// 且主题前缀可自定义。无需 Broker。
/// </summary>
public sealed class MqttOperationCatalogTests
{
    [Theory]
    [InlineData(MesOperationKeys.GetWorkOrder)]
    [InlineData(MesOperationKeys.GetUnit)]
    [InlineData(MesOperationKeys.GetRecipe)]
    [InlineData(MesOperationKeys.GetTraceability)]
    [InlineData(MesOperationKeys.CheckUnitPassed)]
    public void QueryOperations_AreRequestResponse(string operationKey)
    {
        var catalog = MesOperationCatalog.CreateMqttDefaults();

        Assert.True(catalog.TryGet(operationKey, out var binding));
        Assert.True(binding.RequestResponse);
    }

    [Theory]
    [InlineData(MesOperationKeys.ReportInspection)]
    [InlineData(MesOperationKeys.ReportMeasurements)]
    [InlineData(MesOperationKeys.ReportDeviceStatus)]
    [InlineData(MesOperationKeys.RaiseAlarm)]
    public void ReportOperations_ArePublishOnly(string operationKey)
    {
        var catalog = MesOperationCatalog.CreateMqttDefaults();

        Assert.True(catalog.TryGet(operationKey, out var binding));
        Assert.False(binding.RequestResponse);
    }

    [Fact]
    public void CustomPrefix_IsAppliedToChannels()
    {
        var catalog = MesOperationCatalog.CreateMqttDefaults("factory1");

        Assert.True(catalog.TryGet(MesOperationKeys.GetWorkOrder, out var binding));
        Assert.StartsWith("factory1/query/workorder/", binding.ChannelTemplate);
    }
}
