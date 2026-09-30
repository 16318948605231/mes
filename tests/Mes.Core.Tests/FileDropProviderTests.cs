using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Enums;
using Mes.Core.Models;
using Mes.Protocols.FileDrop;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 使用临时交换目录验证文件落地 Provider：上报会在 outbox 目录生成 JSON 文件。
/// </summary>
public sealed class FileDropProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mes-filedrop-{Guid.NewGuid():N}");

    private IMesClient CreateClient()
        => MesClientBuilder.Create().UseFileDrop(_root).Build();

    [Fact]
    public async Task ReportInspection_WritesFileToOutbox()
    {
        var client = CreateClient();

        var result = await client.ReportInspectionResultAsync(new InspectionResult
        {
            SerialNumber = "SN-FD-1",
            Outcome = InspectionOutcome.Pass
        });

        Assert.True(result.Success);
        var outbox = Path.Combine(_root, "outbox", "inspection");
        Assert.True(Directory.Exists(outbox));
        var files = Directory.GetFiles(outbox, "*.json");
        Assert.NotEmpty(files);
        var content = await File.ReadAllTextAsync(files[0]);
        Assert.Contains("SN-FD-1", content);
    }

    [Fact]
    public async Task RaiseAlarm_WritesFileToOutbox()
    {
        var client = CreateClient();

        var result = await client.RaiseAlarmAsync(new Alarm { Code = "E-FD", Text = "test" });

        Assert.True(result.Success);
        var outbox = Path.Combine(_root, "outbox", "alarms");
        Assert.True(Directory.Exists(outbox));
        Assert.NotEmpty(Directory.GetFiles(outbox, "*.json"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }
}
