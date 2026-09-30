using Mes.Core.Enums;

namespace Mes.Core.Models;

/// <summary>
/// 边界框 / 感兴趣区域（图像坐标，单位：像素，除非 <see cref="Unit"/> 另有说明）。
/// </summary>
public sealed class BoundingBox
{
    /// <summary>左上角 X。</summary>
    public double X { get; set; }

    /// <summary>左上角 Y。</summary>
    public double Y { get; set; }

    /// <summary>宽度。</summary>
    public double Width { get; set; }

    /// <summary>高度。</summary>
    public double Height { get; set; }

    /// <summary>坐标单位（默认 px）。</summary>
    public string Unit { get; set; } = "px";

    /// <summary>可选的多边形轮廓点（用于非矩形缺陷区域）。</summary>
    public IList<PointF2D>? Polygon { get; set; }
}

/// <summary>二维点。</summary>
public readonly record struct PointF2D(double X, double Y);

/// <summary>
/// 单项测量值（尺寸、灰度、强度、坐标偏差等）。
/// </summary>
public sealed class Measurement
{
    /// <summary>测量项名称/编码。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>数值（若为数值型测量）。</summary>
    public double? Value { get; set; }

    /// <summary>字符串值（若为非数值型，如颜色名、判定文本）。</summary>
    public string? StringValue { get; set; }

    /// <summary>单位（mm、μm、px、%、°C 等）。</summary>
    public string? Unit { get; set; }

    /// <summary>规格下限（LSL）。</summary>
    public double? LowerLimit { get; set; }

    /// <summary>规格上限（USL）。</summary>
    public double? UpperLimit { get; set; }

    /// <summary>标称/目标值。</summary>
    public double? Nominal { get; set; }

    /// <summary>判定状态。</summary>
    public MeasurementStatus Status { get; set; } = MeasurementStatus.NotEvaluated;

    /// <summary>测量时间。</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();

    /// <summary>
    /// 依据上下限自动计算判定状态（当上下限存在且 <see cref="Value"/> 非空时）。
    /// </summary>
    public MeasurementStatus Evaluate()
    {
        if (Value is null)
            return Status;

        if (LowerLimit is { } lo && Value < lo) return Status = MeasurementStatus.Ng;
        if (UpperLimit is { } hi && Value > hi) return Status = MeasurementStatus.Ng;
        return Status = MeasurementStatus.Ok;
    }
}

/// <summary>
/// 缺陷记录。
/// </summary>
public sealed class Defect
{
    /// <summary>缺陷代码。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>缺陷名称。</summary>
    public string? Name { get; set; }

    /// <summary>缺陷分类（划痕、脏污、崩边、错件等）。</summary>
    public string? Category { get; set; }

    /// <summary>严重度。</summary>
    public DefectSeverity Severity { get; set; } = DefectSeverity.Minor;

    /// <summary>数量。</summary>
    public int Count { get; set; } = 1;

    /// <summary>缺陷区域（可选）。</summary>
    public BoundingBox? Region { get; set; }

    /// <summary>算法置信度（0~1）。</summary>
    public double? Confidence { get; set; }

    /// <summary>关联图像的引用标识（对应 <see cref="MesImage.Id"/>）。</summary>
    public string? ImageRef { get; set; }

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}

/// <summary>
/// 检测结果（机器视觉 / 测试工位上报的核心报文）。
/// </summary>
public sealed class InspectionResult
{
    /// <summary>结果唯一标识（客户端生成，便于幂等与追踪）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>被检单元序列号（SN）。</summary>
    public string? SerialNumber { get; set; }

    /// <summary>工单号。</summary>
    public string? WorkOrderId { get; set; }

    /// <summary>产品编码。</summary>
    public string? ProductCode { get; set; }

    /// <summary>工位/设备标识。</summary>
    public string? StationId { get; set; }

    /// <summary>工序标识。</summary>
    public string? OperationId { get; set; }

    /// <summary>检测程序 / 配方标识。</summary>
    public string? ProgramId { get; set; }

    /// <summary>操作员。</summary>
    public string? OperatorId { get; set; }

    /// <summary>总体结论。</summary>
    public InspectionOutcome Outcome { get; set; } = InspectionOutcome.None;

    /// <summary>检测时间。</summary>
    public DateTimeOffset InspectedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>检测耗时（毫秒）。</summary>
    public double? DurationMs { get; set; }

    /// <summary>测量值集合。</summary>
    public IList<Measurement> Measurements { get; set; } = new List<Measurement>();

    /// <summary>缺陷集合。</summary>
    public IList<Defect> Defects { get; set; } = new List<Defect>();

    /// <summary>图像集合（可分别选择内嵌或单独上传）。</summary>
    public IList<MesImage> Images { get; set; } = new List<MesImage>();

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();

    /// <summary>是否合格（Pass 或 Warning 视为通过）。</summary>
    public bool IsPass => Outcome is InspectionOutcome.Pass or InspectionOutcome.Warning;
}
