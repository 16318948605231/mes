using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Mes.Core.Diagnostics;

/// <summary>
/// 可观测性入口：暴露统一的 <see cref="System.Diagnostics.ActivitySource"/> 与
/// <see cref="System.Diagnostics.Metrics.Meter"/>，供接入方接入 OpenTelemetry / 指标后端。
/// <para>
/// 追踪：源名称 <c>Mes.Client</c>；指标：Meter 名称 <c>Mes.Client</c>，包含
/// <c>mes.client.operations</c>（计数）与 <c>mes.client.operation.duration</c>（毫秒直方图），
/// 维度含 <c>operation</c>、<c>protocol</c>、<c>kind</c>（request/publish）、<c>status</c>（ok/error）。
/// </para>
/// </summary>
public static class MesDiagnostics
{
    /// <summary>诊断源/仪表的公共名称。</summary>
    public const string SourceName = "Mes.Client";

    /// <summary>当前程序集版本（用于标注 ActivitySource/Meter）。</summary>
    public static readonly string Version =
        typeof(MesDiagnostics).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>统一的分布式追踪源。</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    /// <summary>统一的指标仪表。</summary>
    public static readonly Meter Meter = new(SourceName, Version);

    /// <summary>操作计数器（成功/失败均计数，通过 status 维度区分）。</summary>
    public static readonly Counter<long> Operations =
        Meter.CreateCounter<long>("mes.client.operations", unit: "{operation}", description: "MES 客户端操作次数。");

    /// <summary>操作耗时直方图（毫秒）。</summary>
    public static readonly Histogram<double> OperationDuration =
        Meter.CreateHistogram<double>("mes.client.operation.duration", unit: "ms", description: "MES 客户端操作耗时（毫秒）。");

    /// <summary>记录一次操作的指标（计数 + 耗时）。</summary>
    public static void Record(string operation, string protocol, string kind, bool success, double elapsedMs)
    {
        var status = success ? "ok" : "error";
        var opTag = new KeyValuePair<string, object?>("operation", operation);
        var protoTag = new KeyValuePair<string, object?>("protocol", protocol);
        var kindTag = new KeyValuePair<string, object?>("kind", kind);
        var statusTag = new KeyValuePair<string, object?>("status", status);

        Operations.Add(1, opTag, protoTag, kindTag, statusTag);
        OperationDuration.Record(elapsedMs, opTag, protoTag, kindTag, statusTag);
    }

    /// <summary>为一次操作开启追踪 Activity（可能返回 <c>null</c>，取决于是否有监听器）。</summary>
    public static Activity? StartOperation(string operation, string protocol, string kind)
    {
        var activity = ActivitySource.StartActivity($"MES {operation}", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("mes.operation", operation);
            activity.SetTag("mes.protocol", protocol);
            activity.SetTag("mes.kind", kind);
        }
        return activity;
    }
}
