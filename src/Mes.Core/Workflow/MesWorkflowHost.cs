using System.Diagnostics;
using Mes.Core.Client;
using Mes.Core.Common;
using Mes.Core.Configuration;
using Mes.Core.Models;
using Mes.Core.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mes.Core.Workflow;

/// <summary>
/// <see cref="IMesWorkflowHost"/> 的默认实现：按配置的“场景剧本”顺序执行步骤。
/// 复用现有操作目录、模板引擎、客户端与诊断，不改动任何协议 Provider。
/// </summary>
public sealed class MesWorkflowHost : IMesWorkflowHost
{
    private readonly IMesClient _client;
    private readonly bool _enabled;
    private readonly IReadOnlyDictionary<string, List<MesWorkflowStep>> _workflows;
    private readonly ILogger _logger;

    /// <summary>构造。</summary>
    public MesWorkflowHost(IMesClient client, MesOptions options, ILogger<MesWorkflowHost>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(options);
        _enabled = options.Enabled;
        _logger = logger ?? NullLogger<MesWorkflowHost>.Instance;

        var map = new Dictionary<string, List<MesWorkflowStep>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in options.Workflows)
        {
            if (kv.Value is { Count: > 0 })
                map[kv.Key] = kv.Value;
        }
        _workflows = map;
    }

    /// <inheritdoc />
    public bool Enabled => _enabled;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Phases => (IReadOnlyCollection<string>)_workflows.Keys;

    /// <inheritdoc />
    public MesWorkflowContext CreateContext() => new();

    /// <inheritdoc />
    public bool HasPhase(string phase)
        => !string.IsNullOrEmpty(phase) && _workflows.ContainsKey(phase);

    /// <inheritdoc />
    public async Task<MesWorkflowResult> RunAsync(string phase, MesWorkflowContext? context = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phase))
            return MesWorkflowResult.NoOp(phase ?? string.Empty, "阶段名为空。");

        if (!_enabled)
            return MesWorkflowResult.NoOp(phase, "MES 已禁用 (Enabled=false)，空操作。");

        if (!_workflows.TryGetValue(phase, out var steps) || steps.Count == 0)
            return MesWorkflowResult.NoOp(phase, "该阶段未配置步骤，空操作。");

        var ctx = context ?? new MesWorkflowContext();
        var stopwatch = Stopwatch.StartNew();
        var results = new List<MesWorkflowStepResult>(steps.Count);
        var overallSuccess = true;

        for (int i = 0; i < steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = steps[i];
            var stepName = string.IsNullOrWhiteSpace(step.Name) ? step.Operation : step.Name!;

            // 条件判断。
            if (!MesConditionEvaluator.Evaluate(step.When, ctx))
            {
                results.Add(new MesWorkflowStepResult
                {
                    Step = stepName,
                    Operation = step.Operation,
                    Skipped = true,
                    Success = true,
                    Message = $"When 未满足，跳过：{step.When}",
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(step.Operation))
            {
                var msg = $"步骤 #{i + 1} 未指定 Operation。";
                _logger.LogWarning("工作流阶段 {Phase}: {Message}", phase, msg);
                results.Add(new MesWorkflowStepResult
                {
                    Step = stepName,
                    Operation = step.Operation,
                    Success = false,
                    Message = msg,
                });
                if (ApplyFailurePolicy(step, ref overallSuccess))
                    break;
                continue;
            }

            var stepStart = Stopwatch.GetTimestamp();
            MesResult callResult;
            try
            {
                callResult = await ExecuteStepAsync(step, ctx, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                callResult = MesResult.FromException(ex);
            }

            var elapsed = (long)Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds;
            var success = callResult.Success;

            results.Add(new MesWorkflowStepResult
            {
                Step = stepName,
                Operation = step.Operation,
                Success = success,
                Result = callResult,
                Message = callResult.Message,
                ElapsedMilliseconds = elapsed,
            });

            if (!success)
            {
                _logger.LogWarning("工作流阶段 {Phase} 步骤 {Step} 失败：[{Code}] {Message}",
                    phase, stepName, callResult.Code, callResult.Message);

                if (step.OnError == MesWorkflowErrorBehavior.Alarm && !step.Optional)
                    await RaiseStepAlarmAsync(step, callResult, cancellationToken).ConfigureAwait(false);

                if (ApplyFailurePolicy(step, ref overallSuccess))
                    break;
            }
        }

        stopwatch.Stop();
        return new MesWorkflowResult
        {
            Phase = phase,
            Success = overallSuccess,
            Skipped = false,
            Steps = results,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        };
    }

    /// <summary>
    /// 根据失败策略更新整体成败标记，并返回是否应中止后续步骤。
    /// 语义：<c>Optional</c> 最宽松（不影响整体、不中止）；<c>Continue</c>/<c>Alarm</c> 置整体失败但继续；
    /// <c>Stop</c>（默认）置整体失败并中止。
    /// </summary>
    private static bool ApplyFailurePolicy(MesWorkflowStep step, ref bool overallSuccess)
    {
        if (step.Optional)
            return false;

        overallSuccess = false;
        return step.OnError == MesWorkflowErrorBehavior.Stop;
    }

    private async Task<MesResult> ExecuteStepAsync(MesWorkflowStep step, MesWorkflowContext ctx, CancellationToken cancellationToken)
    {
        var args = RenderArgs(step.Args, ctx);
        var payload = ResolvePayload(step, ctx);

        using var linked = step.TimeoutMs is > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (linked is not null)
            linked.CancelAfter(step.TimeoutMs!.Value);
        var token = linked?.Token ?? cancellationToken;

        if (!string.IsNullOrWhiteSpace(step.CaptureTo))
        {
            var typed = await _client.InvokeAsync<Dictionary<string, object?>>(step.Operation, payload, args, token).ConfigureAwait(false);
            if (typed.Success)
                ctx.Set(step.CaptureTo!, typed.Value);
            return typed;
        }

        return await _client.InvokeAsync(step.Operation, payload, args, token).ConfigureAwait(false);
    }

    private static IDictionary<string, object?>? RenderArgs(IDictionary<string, string> args, MesWorkflowContext ctx)
    {
        if (args is null || args.Count == 0)
            return null;

        var result = new Dictionary<string, object?>(args.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in args)
            result[kv.Key] = RenderTemplate(kv.Value, ctx);
        return result;
    }

    private static object? ResolvePayload(MesWorkflowStep step, MesWorkflowContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(step.PayloadFrom))
            return ctx.TryResolvePath(step.PayloadFrom!, out var value) ? value : null;

        if (step.Payload is { Count: > 0 })
        {
            var body = new Dictionary<string, object?>(step.Payload.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in step.Payload)
                body[kv.Key] = RenderTemplate(kv.Value, ctx);
            return body;
        }

        return null;
    }

    /// <summary>渲染模板字符串，占位符从上下文（支持点路径）取值。</summary>
    private static string RenderTemplate(string template, MesWorkflowContext ctx)
    {
        if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0)
            return template;

        var tokens = TemplateEngine.ExtractTokens(template);
        if (tokens.Count == 0)
            return template;

        var flat = new Dictionary<string, object?>(tokens.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var t in tokens)
        {
            if (ctx.TryResolvePath(t, out var value))
                flat[t] = value;
        }

        return TemplateEngine.Render(template, flat);
    }

    private async Task RaiseStepAlarmAsync(MesWorkflowStep step, MesResult cause, CancellationToken cancellationToken)
    {
        try
        {
            var alarm = new Alarm
            {
                Code = $"WORKFLOW_{step.Operation}".ToUpperInvariant(),
                Text = $"工作流步骤 '{step.Operation}' 失败：[{cause.Code}] {cause.Message}",
                Severity = Mes.Core.Enums.AlarmSeverity.Major,
                Category = "Workflow",
            };
            await _client.RaiseAlarmAsync(alarm, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "触发工作流失败报警时出错（操作 {Operation}）。", step.Operation);
        }
    }
}
