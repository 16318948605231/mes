namespace Mes.Core.Workflow;

/// <summary>
/// 步骤失败时的处理策略。
/// </summary>
public enum MesWorkflowErrorBehavior
{
    /// <summary>中止整个阶段，并将阶段结果标记为失败（默认）。</summary>
    Stop = 0,

    /// <summary>记录失败但继续执行后续步骤，阶段整体可仍为成功。</summary>
    Continue = 1,

    /// <summary>触发一条 MES 报警，然后继续执行后续步骤。</summary>
    Alarm = 2,
}
