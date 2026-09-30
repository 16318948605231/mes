namespace Mes.Core.Models;

/// <summary>
/// 物料消耗记录（用于装配/上料追溯）。
/// </summary>
public sealed class MaterialConsumption
{
    /// <summary>物料编码。</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>批次/卷号。</summary>
    public string? Lot { get; set; }

    /// <summary>序列号（若为可序列化物料）。</summary>
    public string? SerialNumber { get; set; }

    /// <summary>数量。</summary>
    public decimal Quantity { get; set; }

    /// <summary>单位。</summary>
    public string? Unit { get; set; }

    /// <summary>供应商。</summary>
    public string? Supplier { get; set; }

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}

/// <summary>
/// 过程步骤（工序历史）。
/// </summary>
public sealed class ProcessStep
{
    /// <summary>工序标识。</summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>工位/设备标识。</summary>
    public string? StationId { get; set; }

    /// <summary>结果（Pass/Fail/…）。</summary>
    public string? Result { get; set; }

    /// <summary>操作员。</summary>
    public string? OperatorId { get; set; }

    /// <summary>时间戳。</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}

/// <summary>
/// 追溯记录（单元谱系）。
/// </summary>
public sealed class TraceabilityRecord
{
    /// <summary>序列号。</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>父序列号（装配到的上级）。</summary>
    public string? ParentSerialNumber { get; set; }

    /// <summary>产品编码。</summary>
    public string? ProductCode { get; set; }

    /// <summary>工单号。</summary>
    public string? WorkOrderId { get; set; }

    /// <summary>子部件序列号集合。</summary>
    public IList<string> Components { get; set; } = new List<string>();

    /// <summary>物料消耗记录。</summary>
    public IList<MaterialConsumption> Materials { get; set; } = new List<MaterialConsumption>();

    /// <summary>工序历史。</summary>
    public IList<ProcessStep> ProcessHistory { get; set; } = new List<ProcessStep>();

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}
