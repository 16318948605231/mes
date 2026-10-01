namespace Mes.Core.Workflow;

/// <summary>
/// 面向应用的极简、跨工厂稳定入口：检测软件只需在启动读配置，在检测前/中/后各触发一次“阶段”，
/// “每个阶段做哪些 MES 交互”全部来自配置文件。换厂只换配置，检测软件代码零改动。
/// </summary>
public interface IMesWorkflowHost
{
    /// <summary>
    /// MES 交互总开关（取自 <c>MesOptions.Enabled</c>）。为 <c>false</c> 时所有
    /// <see cref="RunAsync"/> 调用都会立即返回成功的空操作结果。
    /// </summary>
    bool Enabled { get; }

    /// <summary>已配置的阶段名集合（便于诊断）。</summary>
    IReadOnlyCollection<string> Phases { get; }

    /// <summary>创建一个新的运行时上下文变量袋。</summary>
    MesWorkflowContext CreateContext();

    /// <summary>判断某阶段是否已配置且包含步骤。</summary>
    bool HasPhase(string phase);

    /// <summary>
    /// 执行某阶段的“场景剧本”。<c>Enabled=false</c> 或该阶段未配置/为空 → 立即返回成功（空操作）。
    /// </summary>
    /// <param name="phase">阶段名（应用自定，如 <c>BeforeInspection</c>）。</param>
    /// <param name="context">运行时上下文；为空则内部创建一个空上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<MesWorkflowResult> RunAsync(string phase, MesWorkflowContext? context = null, CancellationToken cancellationToken = default);
}
