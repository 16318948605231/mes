using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Enums;
using Mes.Core.Models;
using Mes.Protocols.InMemory;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 通过进程内回环（InMemory）验证高层客户端的端到端行为：查询、上报、事件、通用出口。
/// </summary>
public sealed class InMemoryClientTests
{
    private static (IMesClient Client, InMemoryMesServer Server) CreateClient()
    {
        var server = new InMemoryMesServer();
        var client = MesClientBuilder.Create()
            .WithName("test")
            .UseInMemory(server)
            .Build();
        return (client, server);
    }

    [Fact]
    public async Task GetWorkOrder_ReturnsPreseededValue()
    {
        var (client, server) = CreateClient();
        server.WorkOrders["WO-1"] = new WorkOrder { Id = "WO-1", ProductCode = "P1", Quantity = 10 };

        var result = await client.GetWorkOrderAsync("WO-1");

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("WO-1", result.Value!.Id);
        Assert.Equal("P1", result.Value.ProductCode);
    }

    [Fact]
    public async Task GetWorkOrder_Missing_ReturnsFailure()
    {
        var (client, _) = CreateClient();

        var result = await client.GetWorkOrderAsync("does-not-exist");

        Assert.False(result.Success);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetUnit_ReturnsPreseededValue()
    {
        var (client, server) = CreateClient();
        server.Units["SN-1"] = new ProductUnit { SerialNumber = "SN-1", ProductCode = "P1", Status = "InProcess" };

        var result = await client.GetUnitAsync("SN-1");

        Assert.True(result.Success);
        Assert.Equal("SN-1", result.Value!.SerialNumber);
    }

    [Fact]
    public async Task CheckUnitPassed_DefaultsToPassed()
    {
        var (client, _) = CreateClient();

        var result = await client.CheckUnitPassedAsync("SN-1", "OP10");

        Assert.True(result.Success);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task CheckUnitPassed_RespectsGateDecision()
    {
        var (client, server) = CreateClient();
        server.GateDecisions["SN-1|OP10"] = false;

        var result = await client.CheckUnitPassedAsync("SN-1", "OP10");

        Assert.True(result.Success);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task ReportInspection_CapturedByServer_AndRaisesEvent()
    {
        var (client, server) = CreateClient();
        var eventRaised = false;
        client.InspectionReported += (_, _) => eventRaised = true;

        var inspection = new InspectionResult
        {
            SerialNumber = "SN-1",
            StationId = "ST-1",
            Outcome = InspectionOutcome.Pass,
            Measurements = { new Measurement { Name = "gap", Value = 1.2, Unit = "mm" } }
        };

        var result = await client.ReportInspectionResultAsync(inspection);

        Assert.True(result.Success);
        Assert.True(server.ReportedInspections.TryDequeue(out var captured));
        Assert.Equal("SN-1", captured!.SerialNumber);
        Assert.True(eventRaised);
    }

    [Fact]
    public async Task RaiseAlarm_CapturedByServer()
    {
        var (client, server) = CreateClient();

        var result = await client.RaiseAlarmAsync(new Alarm { Code = "E-100", Text = "door open", Severity = AlarmSeverity.Critical });

        Assert.True(result.Success);
        Assert.True(server.ReportedAlarms.TryDequeue(out var alarm));
        Assert.Equal("E-100", alarm!.Code);
    }

    [Fact]
    public async Task ReportDeviceStatus_CapturedByServer()
    {
        var (client, server) = CreateClient();

        var result = await client.ReportDeviceStatusAsync(new DeviceStatus { DeviceId = "CAM-1", State = DeviceState.Running });

        Assert.True(result.Success);
        Assert.True(server.ReportedDeviceStatuses.TryDequeue(out var status));
        Assert.Equal("CAM-1", status!.DeviceId);
    }

    [Fact]
    public async Task SubscribeAlarms_ReceivesServerPush()
    {
        var (client, server) = CreateClient();
        var tcs = new TaskCompletionSource<Alarm>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sub = await client.SubscribeAlarmsAsync("alarms", (alarm, _) =>
        {
            tcs.TrySetResult(alarm);
            return Task.CompletedTask;
        });

        await server.PushAsync("alarms", new Alarm { Code = "E-200", Text = "overheat" });

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("E-200", received.Code);
    }

    [Fact]
    public async Task InvokeAsync_Generic_UsesCustomHandler()
    {
        var server = new InMemoryMesServer();
        server.OnRequest("Ping", _ => InMemoryResponse.Ok(new { pong = true, at = "now" }));
        var client = MesClientBuilder.Create()
            .UseInMemory(server)
            .MapOperation("Ping", "Ping", verb: "POST")
            .Build();

        var result = await client.InvokeAsync<Dictionary<string, object?>>("Ping");

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task ConnectAsync_RaisesConnectionStateChanged()
    {
        var (client, _) = CreateClient();
        MesConnectionState? observed = null;
        client.ConnectionStateChanged += (_, e) => observed = e.Current;

        await client.ConnectAsync();

        Assert.Equal(MesConnectionState.Connected, client.State);
        Assert.Equal(MesConnectionState.Connected, observed);
    }
}
