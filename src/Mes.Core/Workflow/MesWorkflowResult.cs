using Mes.Core.Common;

namespace Mes.Core.Workflow;

/// <summary>
/// 单个步骤的执行结果。
/// </summary>
public sealed class MesWorkflowStepResult
{
    /// <summary>步骤名称（取 <see cref="MesWorkflowStep.Name"/>，缺省为操作键）。</summary>
    public string Step { get; init; } = string.Empty;

    /// <summary>操作键。</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>是否被 <c>When</c> 条件跳过。</summary>
    public bool Skipped { get; init; }

    /// <summary>步骤是否成功（跳过的步骤视为成功）。</summary>
    public bool Success { get; init; } = true;

    /// <summary>底层调用结果（跳过时为 <c>null</c>）。</summary>
    public MesResult? Result { get; init; }

    /// <summary>说明信息（如跳过原因、失败原因）。</summary>
    public string? Message { get; init; }

    /// <summary>本步骤耗时（毫秒）。</summary>
    public long ElapsedMilliseconds { get; init; }
}

/// <summary>
/// 一个阶段（场景剧本）的聚合执行结果。
/// </summary>
public sealed class MesWorkflowResult
{
    /// <summary>阶段名。</summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>阶段是否整体成功。</summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// 是否被跳过（总开关 <c>Enabled=false</c>、阶段未配置或为空时为 <c>true</c>，且视为成功）。
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>各步骤结果（按执行顺序）。</summary>
    public IReadOnlyList<MesWorkflowStepResult> Steps { get; init; }
        = Array.Empty<MesWorkflowStepResult>();

    /// <summary>阶段总耗时（毫秒）。</summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>说明信息。</summary>
    public string? Message { get; init; }

    /// <summary>创建一个“空操作/跳过”的成功结果。</summary>
    public static MesWorkflowResult NoOp(string phase, string? message = null)
        => new() { Phase = phase, Success = true, Skipped = true, Message = message };

    /// <inheritdoc />
    public override string ToString()
        => Skipped
            ? $"{Phase}: skipped"
            : $"{Phase}: {(Success ? "OK" : "FAIL")} ({Steps.Count} steps, {ElapsedMilliseconds}ms)";
}
