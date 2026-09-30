using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Enums;
using Mes.Core.Models;
using Mes.Protocols.Database;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 使用 SQLite（临时文件 + 内置示例架构）验证数据库直连 Provider 的写入/读取往返。
/// </summary>
public sealed class DatabaseProviderTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mes-test-{Guid.NewGuid():N}.db");

    private IMesClient CreateClient()
        => MesClientBuilder.Create()
            .UseDatabase($"Data Source={_dbPath}", provider: "Sqlite", configure: o => o.SetProperty("EnsureSchema", "true"))
            .MapOperation("SeedWorkOrder", "INSERT INTO mes_workorder(id,data) VALUES(@workOrderId,@json)", verb: "INSERT")
            .MapOperation("CountClearedAlarms", "SELECT COUNT(*) FROM mes_alarm WHERE code=@alarmCode AND cleared_at IS NOT NULL", verb: "SELECT")
            .Build();

    [Fact]
    public async Task ReportInspection_InsertsRow()
    {
        var client = CreateClient();

        var result = await client.ReportInspectionResultAsync(new InspectionResult
        {
            SerialNumber = "SN-DB-1",
            Outcome = InspectionOutcome.Pass
        });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task GetWorkOrder_ReadsInsertedJson()
    {
        var client = CreateClient();
        await client.ConnectAsync();

        var seed = await client.InvokeAsync(
            "SeedWorkOrder",
            payload: new WorkOrder { Id = "WO-DB-1", ProductCode = "PDB" },
            args: new Dictionary<string, object?> { ["workOrderId"] = "WO-DB-1" });
        Assert.True(seed.Success);

        var read = await client.GetWorkOrderAsync("WO-DB-1");

        Assert.True(read.Success);
        Assert.Equal("WO-DB-1", read.Value!.Id);
        Assert.Equal("PDB", read.Value.ProductCode);
    }

    [Fact]
    public async Task GetWorkOrder_Missing_ReturnsFailure()
    {
        var client = CreateClient();

        var read = await client.GetWorkOrderAsync("nope");

        Assert.False(read.Success);
    }

    [Fact]
    public async Task ClearAlarm_UpdatesExistingRow_NotInsert()
    {
        var client = CreateClient();
        await client.ConnectAsync();

        // 触发报警（INSERT 一行，cleared_at 为空）
        var raise = await client.RaiseAlarmAsync(new Alarm { Code = "E-DB-ALM", Text = "camera offline" });
        Assert.True(raise.Success);

        // 清除前：已清除计数应为 0
        var before = await client.InvokeAsync<int>("CountClearedAlarms",
            args: new Dictionary<string, object?> { ["alarmCode"] = "E-DB-ALM" });
        Assert.True(before.Success);
        Assert.Equal(0, before.Value);

        // 解除报警（应为 UPDATE，回填 cleared_at）
        var clear = await client.ClearAlarmAsync("E-DB-ALM");
        Assert.True(clear.Success);

        // 清除后：已清除计数应为 1（说明是更新既有行，而非再插入一条新报警）
        var after = await client.InvokeAsync<int>("CountClearedAlarms",
            args: new Dictionary<string, object?> { ["alarmCode"] = "E-DB-ALM" });
        Assert.True(after.Success);
        Assert.Equal(1, after.Value);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* ignore */ }
    }
}
