using System.Text;
using Mes.Core.Configuration;
using Mes.Core.Workflow;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 验证“场景剧本”可从 appsettings.json 配置绑定，并经非 DI 入口 <c>MesWorkflows.Create</c> 构建宿主。
/// </summary>
public sealed class WorkflowConfigurationTests
{
    private const string Json = """
    {
      "Mes": {
        "Enabled": true,
        "ConnectionString": "mem://unit-test",
        "Workflows": {
          "BeforeInspection": [
            { "Operation": "GetWorkOrder", "Args": { "workOrderId": "{workOrderId}" }, "CaptureTo": "workOrder" },
            { "Operation": "GetRecipe", "Args": { "recipeId": "{recipeId}" }, "CaptureTo": "recipe", "OnError": "Continue" }
          ],
          "AfterInspection": [
            { "Operation": "ReportInspection", "PayloadFrom": "inspectionResult" }
          ],
          "OnFail": [
            { "Operation": "RaiseAlarm", "When": "outcome == Fail", "Payload": { "code": "AOI_NG", "text": "SN {serialNumber} 不合格" } }
          ]
        }
      }
    }
    """;

    private static IConfiguration BuildConfig(string json)
        => new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

    [Fact]
    public void Binds_Enabled_And_Workflows_From_Json()
    {
        var config = BuildConfig(Json);
        var options = new MesOptions();
        config.GetSection("Mes").Bind(options);

        Assert.True(options.Enabled);
        Assert.Equal(3, options.Workflows.Count);

        var before = options.Workflows["BeforeInspection"];
        Assert.Equal(2, before.Count);
        Assert.Equal("GetWorkOrder", before[0].Operation);
        Assert.Equal("{workOrderId}", before[0].Args["workOrderId"]);
        Assert.Equal("workOrder", before[0].CaptureTo);
        Assert.Equal(MesWorkflowErrorBehavior.Continue, before[1].OnError);

        var onFail = options.Workflows["OnFail"];
        Assert.Equal("outcome == Fail", onFail[0].When);
        Assert.NotNull(onFail[0].Payload);
        Assert.Equal("AOI_NG", onFail[0].Payload!["code"]);

        var after = options.Workflows["AfterInspection"];
        Assert.Equal("inspectionResult", after[0].PayloadFrom);
    }

    [Fact]
    public void MesWorkflows_Create_FromConfiguration_BuildsEnabledHost()
    {
        var config = BuildConfig(Json);

        var host = Mes.MesWorkflows.Create(config);

        Assert.True(host.Enabled);
        Assert.True(host.HasPhase("BeforeInspection"));
        Assert.True(host.HasPhase("AfterInspection"));
        Assert.True(host.HasPhase("OnFail"));
    }

    [Fact]
    public async Task MesWorkflows_Create_Disabled_IsNoOp()
    {
        var json = Json.Replace("\"Enabled\": true", "\"Enabled\": false");
        var config = BuildConfig(json);

        var host = Mes.MesWorkflows.Create(config);
        Assert.False(host.Enabled);

        var result = await host.RunAsync("BeforeInspection", host.CreateContext());
        Assert.True(result.Success);
        Assert.True(result.Skipped);
        Assert.Empty(result.Steps);
    }
}
