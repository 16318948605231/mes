namespace Mes.Core.Workflow;

/// <summary>
/// 一个“场景剧本”步骤：声明在某阶段要执行的一次 MES 交互。
/// 全部字段除 <see cref="Operation"/> 外均为可选；从配置文件绑定，或在代码中构造。
/// </summary>
public sealed class MesWorkflowStep
{
    /// <summary>
    /// 操作键：内置操作键（如 <c>GetWorkOrder</c>、<c>ReportInspection</c>、<c>RaiseAlarm</c>）
    /// 或经 <c>OperationBindings</c> 映射的自定义键。
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// 参数：名称 → 模板字符串。占位符 <c>{var}</c> 会从运行时上下文取值（复用模板引擎）。
    /// 渲染后作为 <c>InvokeAsync</c> 的 <c>args</c>（参与通道模板渲染，未被消费的作为请求参数）。
    /// </summary>
    public IDictionary<string, string> Args { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 内联请求体：字段名 → 模板字符串。渲染后组装为对象作为请求体。
    /// 与 <see cref="PayloadFrom"/> 互斥（<see cref="PayloadFrom"/> 优先）。
    /// </summary>
    public IDictionary<string, string>? Payload { get; set; }

    /// <summary>
    /// 请求体来源：引用上下文中的某个对象变量名（如 <c>inspectionResult</c>）作为请求体。
    /// </summary>
    public string? PayloadFrom { get; set; }

    /// <summary>
    /// 条件表达式：满足时才执行该步骤（如 <c>outcome == Fail</c>、<c>isPass == false</c>、
    /// <c>defectCount &gt; 0</c>、<c>enabled</c>、<c>!isPass</c>）。为空表示始终执行。
    /// </summary>
    public string? When { get; set; }

    /// <summary>
    /// 把本步骤的返回值写入上下文变量名，供后续步骤以 <c>{name}</c> 或 <c>{name.field}</c> 引用，实现串联。
    /// </summary>
    public string? CaptureTo { get; set; }

    /// <summary>步骤失败时的处理策略，默认 <see cref="MesWorkflowErrorBehavior.Stop"/>。</summary>
    public MesWorkflowErrorBehavior OnError { get; set; } = MesWorkflowErrorBehavior.Stop;

    /// <summary>
    /// 是否为可选步骤。可选步骤失败不会使阶段失败（等价于 <see cref="MesWorkflowErrorBehavior.Continue"/> 的语义便捷开关）。
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>本步骤的超时（毫秒）。为空则使用客户端默认超时。</summary>
    public int? TimeoutMs { get; set; }

    /// <summary>可选的步骤名称/备注，便于诊断与结果阅读。</summary>
    public string? Name { get; set; }
}
