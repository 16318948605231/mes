using Mes.Core.Common;
using Mes.Core.Enums;
using Mes.Core.Events;
using Mes.Core.Models;
using Mes.Core.Transport;

namespace Mes.Core.Client;

/// <summary>
/// MES 高层客户端。屏蔽协议差异，提供面向机器视觉/制造业务的统一 API。
/// 这是应用代码主要使用的入口。
/// </summary>
public interface IMesClient : IAsyncDisposable
{
    /// <summary>客户端名称。</summary>
    string Name { get; }

    /// <summary>当前连接状态。</summary>
    MesConnectionState State { get; }

    /// <summary>底层传输（高级用法/诊断）。</summary>
    IMesTransport Transport { get; }

    // ---- 连接管理 ----

    /// <summary>建立连接。</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>断开连接。</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    // ---- 查询类操作 ----

    /// <summary>查询工单。</summary>
    Task<MesResult<WorkOrder>> GetWorkOrderAsync(string workOrderId, CancellationToken cancellationToken = default);

    /// <summary>查询生产单元/序列号。</summary>
    Task<MesResult<ProductUnit>> GetUnitAsync(string serialNumber, CancellationToken cancellationToken = default);

    /// <summary>查询配方。</summary>
    Task<MesResult<Recipe>> GetRecipeAsync(string recipeId, CancellationToken cancellationToken = default);

    /// <summary>查询追溯记录。</summary>
    Task<MesResult<TraceabilityRecord>> GetTraceabilityAsync(string serialNumber, CancellationToken cancellationToken = default);

    /// <summary>过站校验：判断单元是否可在指定工序放行。</summary>
    Task<MesResult<bool>> CheckUnitPassedAsync(string serialNumber, string operationId, CancellationToken cancellationToken = default);

    // ---- 上报类操作 ----

    /// <summary>
    /// 上报检测结果。会依据每张图像的传输模式自动内嵌或先行单独上传。
    /// </summary>
    Task<MesResult> ReportInspectionResultAsync(InspectionResult result, CancellationToken cancellationToken = default);

    /// <summary>上报一批测量值。</summary>
    Task<MesResult> ReportMeasurementsAsync(string serialNumber, IEnumerable<Measurement> measurements, CancellationToken cancellationToken = default);

    /// <summary>上传单张图像，返回上传回执。</summary>
    Task<MesResult<ImageUploadReceipt>> UploadImageAsync(MesImage image, CancellationToken cancellationToken = default);

    /// <summary>上报设备状态。</summary>
    Task<MesResult> ReportDeviceStatusAsync(DeviceStatus status, CancellationToken cancellationToken = default);

    /// <summary>触发报警。</summary>
    Task<MesResult> RaiseAlarmAsync(Alarm alarm, CancellationToken cancellationToken = default);

    /// <summary>解除报警。</summary>
    Task<MesResult> ClearAlarmAsync(string alarmCode, CancellationToken cancellationToken = default);

    // ---- 通用出口 ----

    /// <summary>
    /// 调用任意已映射的操作并反序列化返回值。
    /// </summary>
    Task<MesResult<TResponse>> InvokeAsync<TResponse>(string operationKey, object? payload = null, IDictionary<string, object?>? args = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 调用任意已映射的操作（无返回值）。
    /// </summary>
    Task<MesResult> InvokeAsync(string operationKey, object? payload = null, IDictionary<string, object?>? args = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅频道并将消息反序列化为 <typeparamref name="T"/>。返回可释放的订阅句柄。
    /// </summary>
    Task<IAsyncDisposable> SubscribeAsync<T>(string channel, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default);

    /// <summary>订阅报警频道；收到的报警会触发 <see cref="AlarmReceived"/> 事件并回调处理器。</summary>
    Task<IAsyncDisposable> SubscribeAlarmsAsync(string channel, Func<Alarm, CancellationToken, Task>? handler = null, CancellationToken cancellationToken = default);

    // ---- 事件 ----

    /// <summary>连接状态变化。</summary>
    event EventHandler<MesConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>收到订阅消息。</summary>
    event EventHandler<MesMessageReceivedEventArgs>? MessageReceived;

    /// <summary>收到报警。</summary>
    event EventHandler<MesAlarmEventArgs>? AlarmReceived;

    /// <summary>检测结果已上报。</summary>
    event EventHandler<MesInspectionReportedEventArgs>? InspectionReported;

    /// <summary>发生错误。</summary>
    event EventHandler<MesErrorEventArgs>? ErrorOccurred;
}
