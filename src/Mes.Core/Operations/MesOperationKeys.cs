namespace Mes.Core.Operations;

/// <summary>
/// 内置高层操作键。具体传输通过 <see cref="MesOperationCatalog"/> 将其映射到实际通道/端点。
/// 用户可自定义扩展任意操作键。
/// </summary>
public static class MesOperationKeys
{
    /// <summary>查询工单。</summary>
    public const string GetWorkOrder = "GetWorkOrder";

    /// <summary>查询生产单元/序列号。</summary>
    public const string GetUnit = "GetUnit";

    /// <summary>上报检测结果。</summary>
    public const string ReportInspection = "ReportInspection";

    /// <summary>上报测量值。</summary>
    public const string ReportMeasurements = "ReportMeasurements";

    /// <summary>上传图像。</summary>
    public const string UploadImage = "UploadImage";

    /// <summary>上报设备状态。</summary>
    public const string ReportDeviceStatus = "ReportDeviceStatus";

    /// <summary>触发报警。</summary>
    public const string RaiseAlarm = "RaiseAlarm";

    /// <summary>解除报警。</summary>
    public const string ClearAlarm = "ClearAlarm";

    /// <summary>查询配方。</summary>
    public const string GetRecipe = "GetRecipe";

    /// <summary>查询追溯记录。</summary>
    public const string GetTraceability = "GetTraceability";

    /// <summary>过站校验（判断单元是否可在指定工序放行）。</summary>
    public const string CheckUnitPassed = "CheckUnitPassed";
}
