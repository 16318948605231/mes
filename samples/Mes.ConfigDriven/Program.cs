using Mes;
using Mes.Core.Enums;
using Mes.Core.Models;
using Mes.Core.Workflow;
using Microsoft.Extensions.Configuration;

// ---------------------------------------------------------------------------
// 配置驱动的检测流程（“场景剧本”）示例 · 跨平台控制台
//
// 这段“外观检测软件”的代码**不包含任何 MES 业务细节**：它只做三件事
//   1) 启动时读配置：var mes = MesWorkflows.Create(configuration);
//   2) 在检测前/中/后等阶段调用：await mes.RunAsync("BeforeInspection", ctx);
//   3) 把运行时数据（SN、工单、检测结果…）放进上下文 ctx。
// “每个阶段具体做哪些 MES 交互”全部写在 appsettings.json 的 Mes.Workflows 里。
//
// 换工厂 = 换一份 appsettings（默认 mem://；可选 FactoryA=REST / FactoryB=MQTT），
// 本文件一行都不用改。把 Mes.Enabled 置 false，所有 RunAsync 变成安全空操作。
//
// 运行：
//   dotnet run                      # 使用默认 appsettings.json（mem://，可直接跑）
//   dotnet run -- FactoryA          # 叠加 appsettings.FactoryA.json（REST，需真实服务器）
//   dotnet run -- FactoryB          # 叠加 appsettings.FactoryB.json（MQTT，需真实 Broker）
// ---------------------------------------------------------------------------

Console.OutputEncoding = System.Text.Encoding.UTF8;

var factory = args.Length > 0 ? args[0] : null;   // 可选：FactoryA / FactoryB
var baseDir = AppContext.BaseDirectory;

var builder = new ConfigurationBuilder()
    .SetBasePath(baseDir)
    .AddJsonFile("appsettings.json", optional: false);
if (!string.IsNullOrWhiteSpace(factory))
    builder.AddJsonFile($"appsettings.{factory}.json", optional: false);
IConfiguration configuration = builder.Build();

Console.WriteLine("=== 配置驱动的检测流程（场景剧本）示例 ===");
Console.WriteLine(factory is null
    ? "配置来源：appsettings.json（默认，mem:// 进程内协议）\n"
    : $"配置来源：appsettings.json + appsettings.{factory}.json\n");

// —— 检测软件启动：只需这一行就得到一个“配置驱动”的 MES 宿主 ——
IMesWorkflowHost mes = MesWorkflows.Create(configuration);
Console.WriteLine($"MES 启用：{mes.Enabled}；已配置阶段：{string.Join(", ", mes.Phases)}\n");

// —— OnStartup：软件就绪心跳（上报内容由配置决定）——
var startup = new MesWorkflowContext()
    .Set("startupHeartbeat", new InspectionResult
    {
        StationId = "AOI-01",
        Outcome = InspectionOutcome.None,
        SerialNumber = "STARTUP",
    });
await PrintPhase(mes, "OnStartup", startup);

// —— 模拟两件产品：一件合格、一件不合格 ——
await InspectOne(mes, serialNumber: "SN-0001", pass: true);
await InspectOne(mes, serialNumber: "SN-0002", pass: false);

Console.WriteLine("完成。切换工厂只需换一份 appsettings，这段代码无需改动。");

// 单件检测生命周期：全部通过“阶段 + 上下文”驱动，软件不关心底层协议与具体操作。
static async Task InspectOne(IMesWorkflowHost mes, string serialNumber, bool pass)
{
    Console.WriteLine($"\n──── 检测单元 {serialNumber}（预期 {(pass ? "合格" : "不合格")}）────");

    // 1) 检测前：软件把已知的运行时信息放进上下文。
    var ctx = new MesWorkflowContext()
        .Set("serialNumber", serialNumber)
        .Set("operationId", "OP-AOI")
        .Set("workOrderId", "WO-1001")
        .Set("recipeId", "RC-APPEARANCE");
    await PrintPhase(mes, "BeforeInspection", ctx);

    // 2) 检测中：软件执行自己的视觉算法（此处省略），得到结论。
    var outcome = pass ? InspectionOutcome.Pass : InspectionOutcome.Fail;
    ctx.Set("outcome", outcome.ToString());

    // 3) 把检测结果放进上下文，交给配置决定如何上报。
    var inspection = new InspectionResult
    {
        SerialNumber = serialNumber,
        WorkOrderId = "WO-1001",
        StationId = "AOI-01",
        OperationId = "OP-AOI",
        Outcome = outcome,
    };
    ctx.Set("inspectionResult", inspection);
    ctx.Set("measurementsPayload", new { serialNumber, values = new[] { 1.98, 2.01, 2.00 } });

    // 4) 检测后 + 合格/不合格分支：做哪些 MES 交互，全由配置决定。
    await PrintPhase(mes, "AfterInspection", ctx);
    await PrintPhase(mes, outcome == InspectionOutcome.Pass ? "OnPass" : "OnFail", ctx);
}

static async Task PrintPhase(IMesWorkflowHost mes, string phase, MesWorkflowContext ctx)
{
    var result = await mes.RunAsync(phase, ctx);
    if (result.Skipped)
    {
        Console.WriteLine($"  [{phase}] 跳过（{result.Message}）");
        return;
    }

    Console.WriteLine($"  [{phase}] 整体{(result.Success ? "成功" : "失败")}，{result.Steps.Count} 步，{result.ElapsedMilliseconds}ms");
    foreach (var s in result.Steps)
    {
        var state = s.Skipped ? "跳过" : (s.Success ? "成功" : "失败");
        var extra = s.Skipped ? s.Message : s.Result?.Code;
        Console.WriteLine($"      - {s.Operation,-18} {state}  {extra}");
    }
}
