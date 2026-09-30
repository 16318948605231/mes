using Mes.Core.Enums;

namespace Mes.Core.Models;

/// <summary>
/// 工单。
/// </summary>
public sealed class WorkOrder
{
    /// <summary>工单内部标识。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>工单号（业务编号）。</summary>
    public string? Number { get; set; }

    /// <summary>产品编码。</summary>
    public string? ProductCode { get; set; }

    /// <summary>产品名称。</summary>
    public string? ProductName { get; set; }

    /// <summary>计划数量。</summary>
    public decimal Quantity { get; set; }

    /// <summary>已完成数量。</summary>
    public decimal CompletedQuantity { get; set; }

    /// <summary>状态。</summary>
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Unknown;

    /// <summary>计划开始时间。</summary>
    public DateTimeOffset? PlannedStart { get; set; }

    /// <summary>计划结束时间。</summary>
    public DateTimeOffset? PlannedEnd { get; set; }

    /// <summary>工艺路线标识。</summary>
    public string? RouteId { get; set; }

    /// <summary>当前工序标识。</summary>
    public string? CurrentOperation { get; set; }

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}

/// <summary>
/// 生产单元 / 序列号实体。
/// </summary>
public sealed class ProductUnit
{
    /// <summary>序列号（SN）。</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>所属工单。</summary>
    public string? WorkOrderId { get; set; }

    /// <summary>产品编码。</summary>
    public string? ProductCode { get; set; }

    /// <summary>当前状态（业务定义，如 InProcess/Pass/Fail/Hold）。</summary>
    public string? Status { get; set; }

    /// <summary>当前所在工序。</summary>
    public string? CurrentOperation { get; set; }

    /// <summary>下线/生成时间。</summary>
    public DateTimeOffset? BirthTime { get; set; }

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}

/// <summary>
/// 设备状态。
/// </summary>
public sealed class DeviceStatus
{
    /// <summary>设备标识。</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>状态。</summary>
    public DeviceState State { get; set; } = DeviceState.Unknown;

    /// <summary>状态原因/描述。</summary>
    public string? StateReason { get; set; }

    /// <summary>当前配方/程序。</summary>
    public string? CurrentRecipe { get; set; }

    /// <summary>稼动率（0~1，可选）。</summary>
    public double? Utilization { get; set; }

    /// <summary>时间戳。</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>额外指标（温度、节拍、计数等）。</summary>
    public IDictionary<string, object?> Metrics { get; set; } = new Dictionary<string, object?>();

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}

/// <summary>
/// 报警。
/// </summary>
public sealed class Alarm
{
    /// <summary>报警标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>报警代码。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>报警文本。</summary>
    public string? Text { get; set; }

    /// <summary>严重度。</summary>
    public AlarmSeverity Severity { get; set; } = AlarmSeverity.Warning;

    /// <summary>状态。</summary>
    public AlarmState State { get; set; } = AlarmState.Set;

    /// <summary>来源设备。</summary>
    public string? DeviceId { get; set; }

    /// <summary>分类。</summary>
    public string? Category { get; set; }

    /// <summary>触发时间。</summary>
    public DateTimeOffset RaisedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>解除时间。</summary>
    public DateTimeOffset? ClearedAt { get; set; }

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}

/// <summary>
/// 配方/参数集。
/// </summary>
public sealed class Recipe
{
    /// <summary>配方标识。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>配方名称。</summary>
    public string? Name { get; set; }

    /// <summary>版本。</summary>
    public string? Version { get; set; }

    /// <summary>适用产品编码。</summary>
    public string? ProductCode { get; set; }

    /// <summary>参数字典。</summary>
    public IDictionary<string, object?> Parameters { get; set; } = new Dictionary<string, object?>();

    /// <summary>校验和（用于一致性校验）。</summary>
    public string? Checksum { get; set; }

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}
