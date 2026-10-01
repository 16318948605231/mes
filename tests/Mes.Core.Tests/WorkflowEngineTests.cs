using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Models;
using Mes.Core.Workflow;
using Mes.Protocols.InMemory;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 以进程内回环（InMemory）端到端验证配置驱动的工作流引擎：
/// 正确操作被调用、参数来自上下文、条件生效、CaptureTo 串联、Enabled=false 空操作、OnError 策略正确。
/// </summary>
public sealed class WorkflowEngineTests
{
    private static (IMesWorkflowHost Host, InMemoryMesServer Server) Create(MesOptions options)
    {
        var server = new InMemoryMesServer();
        var client = MesClientBuilder.Create().WithName("wf-test").UseInMemory(server).Build();
        var host = new MesWorkflowHost(client, options);
        return (host, server);
    }

    private static MesOptions OptionsWith(bool enabled, params (string Phase, MesWorkflowStep[] Steps)[] phases)
    {
        var opt = new MesOptions { Enabled = enabled };
        foreach (var (phase, steps) in phases)
            opt.Workflows[phase] = steps.ToList();
        return opt;
    }

    [Fact]
    public async Task EnabledFalse_IsNoOp()
    {
        var options = OptionsWith(false,
            ("BeforeInspection", new[]
            {
                new MesWorkflowStep { Operation = "GetWorkOrder", Args = { ["workOrderId"] = "{workOrderId}" } },
            }));
        var (host, server) = Create(options);
        server.WorkOrders["WO-1"] = new WorkOrder { Id = "WO-1" };

        var ctx = host.CreateContext().Set("workOrderId", "WO-1");
        var result = await host.RunAsync("BeforeInspection", ctx);

        Assert.True(result.Success);
        Assert.True(result.Skipped);
        Assert.Empty(result.Steps);
        Assert.False(host.Enabled);
    }

    [Fact]
    public async Task UnknownPhase_IsNoOp()
    {
        var (host, _) = Create(OptionsWith(true));

        var result = await host.RunAsync("NotConfigured", host.CreateContext());

        Assert.True(result.Success);
        Assert.True(result.Skipped);
    }

    [Fact]
    public async Task CallsCorrectOperation_WithArgsFromContext_AndCaptures()
    {
        var options = OptionsWith(true,
            ("BeforeInspection", new[]
            {
                new MesWorkflowStep
                {
                    Operation = "GetWorkOrder",
                    Args = { ["workOrderId"] = "{workOrderId}" },
                    CaptureTo = "workOrder",
                },
            }));
        var (host, server) = Create(options);
        server.WorkOrders["WO-42"] = new WorkOrder { Id = "WO-42", ProductCode = "P-X", Quantity = 5 };

        var ctx = host.CreateContext().Set("workOrderId", "WO-42");
        var result = await host.RunAsync("BeforeInspection", ctx);

        Assert.True(result.Success);
        Assert.False(result.Skipped);
        Assert.Single(result.Steps);
        Assert.True(result.Steps[0].Success);

        // CaptureTo 串联：上下文可用点路径读取捕获对象字段。
        Assert.True(ctx.TryResolvePath("workOrder.productCode", out var pc));
        Assert.Equal("P-X", pc);
    }

    [Fact]
    public async Task CaptureTo_ChainsAcrossSteps()
    {
        var options = OptionsWith(true,
            ("BeforeInspection", new[]
            {
                new MesWorkflowStep
                {
                    Operation = "GetRecipe",
                    Args = { ["recipeId"] = "{recipeId}" },
                    CaptureTo = "recipe",
                },
                // 第二步使用第一步捕获的配方字段作为参数。
                new MesWorkflowStep
                {
                    Operation = "GetWorkOrder",
                    Args = { ["workOrderId"] = "{recipe.name}" },
                    CaptureTo = "workOrder",
                },
            }));
        var (host, server) = Create(options);
        server.Recipes["R-1"] = new Recipe { Id = "R-1", Name = "WO-FROM-RECIPE" };
        server.WorkOrders["WO-FROM-RECIPE"] = new WorkOrder { Id = "WO-FROM-RECIPE", ProductCode = "chained" };

        var ctx = host.CreateContext().Set("recipeId", "R-1");
        var result = await host.RunAsync("BeforeInspection", ctx);

        Assert.True(result.Success);
        Assert.Equal(2, result.Steps.Count);
        Assert.True(ctx.TryResolvePath("workOrder.productCode", out var pc));
        Assert.Equal("chained", pc);
    }

    [Fact]
    public async Task When_SkipsStep_WhenConditionFalse()
    {
        var options = OptionsWith(true,
            ("AfterInspection", new[]
            {
                new MesWorkflowStep
                {
                    Operation = "RaiseAlarm",
                    When = "outcome == Fail",
                    Payload = new Dictionary<string, string> { ["code"] = "AOI_NG" },
                },
            }));
        var (host, server) = Create(options);

        var ctx = host.CreateContext().Set("outcome", "Pass");
        var result = await host.RunAsync("AfterInspection", ctx);

        Assert.True(result.Success);
        Assert.Single(result.Steps);
        Assert.True(result.Steps[0].Skipped);
        Assert.Empty(server.ReportedAlarms);
    }

    [Fact]
    public async Task When_ExecutesStep_WhenConditionTrue()
    {
        var options = OptionsWith(true,
            ("AfterInspection", new[]
            {
                new MesWorkflowStep
                {
                    Operation = "RaiseAlarm",
                    When = "outcome == Fail",
                    Payload = new Dictionary<string, string> { ["code"] = "AOI_NG", ["text"] = "SN {serialNumber} 不合格" },
                },
            }));
        var (host, server) = Create(options);

        var ctx = host.CreateContext().Set("outcome", "Fail").Set("serialNumber", "SN-9");
        var result = await host.RunAsync("AfterInspection", ctx);

        Assert.True(result.Success);
        Assert.False(result.Steps[0].Skipped);
        Assert.Single(server.ReportedAlarms);
        Assert.True(server.ReportedAlarms.TryDequeue(out var alarm));
        Assert.Equal("AOI_NG", alarm!.Code);
        Assert.Equal("SN SN-9 不合格", alarm.Text);
    }

    [Fact]
    public async Task PayloadFrom_SendsContextObject()
    {
        var options = OptionsWith(true,
            ("AfterInspection", new[]
            {
                new MesWorkflowStep { Operation = "ReportInspection", PayloadFrom = "inspectionResult" },
            }));
        var (host, server) = Create(options);

        var inspection = new InspectionResult { SerialNumber = "SN-1", Outcome = Enums.InspectionOutcome.Pass };
        var ctx = host.CreateContext().Set("inspectionResult", inspection);
        var result = await host.RunAsync("AfterInspection", ctx);

        Assert.True(result.Success);
        Assert.Single(server.ReportedInspections);
        Assert.True(server.ReportedInspections.TryDequeue(out var reported));
        Assert.Equal("SN-1", reported!.SerialNumber);
    }

    [Fact]
    public async Task OnError_Stop_AbortsSubsequentSteps()
    {
        var options = OptionsWith(true,
            ("BeforeInspection", new[]
            {
                new MesWorkflowStep
                {
                    Operation = "GetWorkOrder",
                    Args = { ["workOrderId"] = "{workOrderId}" }, // 未预置 → 失败
                    OnError = MesWorkflowErrorBehavior.Stop,
                },
                new MesWorkflowStep { Operation = "ReportInspection", PayloadFrom = "inspectionResult" },
            }));
        var (host, server) = Create(options);

        var ctx = host.CreateContext()
            .Set("workOrderId", "missing")
            .Set("inspectionResult", new InspectionResult { SerialNumber = "SN-1" });
        var result = await host.RunAsync("BeforeInspection", ctx);

        Assert.False(result.Success);
        Assert.Single(result.Steps); // 第二步未执行
        Assert.Empty(server.ReportedInspections);
    }

    [Fact]
    public async Task OnError_Continue_RunsSubsequentSteps()
    {
        var options = OptionsWith(true,
            ("BeforeInspection", new[]
            {
                new MesWorkflowStep
                {
                    Operation = "GetWorkOrder",
                    Args = { ["workOrderId"] = "{workOrderId}" }, // 失败
                    OnError = MesWorkflowErrorBehavior.Continue,
                },
                new MesWorkflowStep { Operation = "ReportInspection", PayloadFrom = "inspectionResult" },
            }));
        var (host, server) = Create(options);

        var ctx = host.CreateContext()
            .Set("workOrderId", "missing")
            .Set("inspectionResult", new InspectionResult { SerialNumber = "SN-1" });
        var result = await host.RunAsync("BeforeInspection", ctx);

        Assert.False(result.Success); // 整体失败（有步骤失败）
        Assert.Equal(2, result.Steps.Count);
        Assert.False(result.Steps[0].Success);
        Assert.True(result.Steps[1].Success);
        Assert.Single(server.ReportedInspections); // 第二步仍执行
    }

    [Fact]
    public async Task OnError_Alarm_RaisesAlarmAndContinues()
    {
        var options = OptionsWith(true,
            ("BeforeInspection", new[]
            {
                new MesWorkflowStep
                {
                    Operation = "GetWorkOrder",
                    Args = { ["workOrderId"] = "{workOrderId}" }, // 失败
                    OnError = MesWorkflowErrorBehavior.Alarm,
                },
                new MesWorkflowStep { Operation = "ReportInspection", PayloadFrom = "inspectionResult" },
            }));
        var (host, server) = Create(options);

        var ctx = host.CreateContext()
            .Set("workOrderId", "missing")
            .Set("inspectionResult", new InspectionResult { SerialNumber = "SN-1" });
        var result = await host.RunAsync("BeforeInspection", ctx);

        Assert.False(result.Success);
        Assert.Equal(2, result.Steps.Count);
        Assert.Single(server.ReportedInspections);      // 后续步骤继续执行
        Assert.Single(server.ReportedAlarms);           // 触发了一条报警
    }

    [Fact]
    public async Task Optional_Failure_DoesNotFailPhase()
    {
        var options = OptionsWith(true,
            ("BeforeInspection", new[]
            {
                new MesWorkflowStep
                {
                    Operation = "GetRecipe",
                    Args = { ["recipeId"] = "{recipeId}" }, // 失败
                    Optional = true,
                },
                new MesWorkflowStep { Operation = "ReportInspection", PayloadFrom = "inspectionResult" },
            }));
        var (host, server) = Create(options);

        var ctx = host.CreateContext()
            .Set("recipeId", "missing")
            .Set("inspectionResult", new InspectionResult { SerialNumber = "SN-1" });
        var result = await host.RunAsync("BeforeInspection", ctx);

        Assert.True(result.Success); // 可选步骤失败不影响整体
        Assert.Equal(2, result.Steps.Count);
        Assert.Single(server.ReportedInspections);
    }

    [Fact]
    public async Task HasPhase_And_Phases_Reflect_Config()
    {
        var options = OptionsWith(true,
            ("OnStartup", new[] { new MesWorkflowStep { Operation = "GetWorkOrder" } }),
            ("EmptyPhase", Array.Empty<MesWorkflowStep>()));
        var (host, _) = Create(options);

        Assert.True(host.HasPhase("OnStartup"));
        Assert.True(host.HasPhase("onstartup")); // 不区分大小写
        Assert.False(host.HasPhase("EmptyPhase")); // 空步骤阶段不算
        Assert.Contains("OnStartup", host.Phases);
    }
}
