using System.Collections.Concurrent;
using Mes.Core.Models;
using Mes.Core.Operations;
using Mes.Core.Serialization;
using Mes.Core.Transport;

namespace Mes.Protocols.InMemory;

/// <summary>
/// 进程内回环的操作结果。
/// </summary>
public readonly record struct InMemoryResponse(int StatusCode, object? Result = null, string? Error = null)
{
    /// <summary>成功结果。</summary>
    public static InMemoryResponse Ok(object? result = null, int statusCode = 200) => new(statusCode, result);

    /// <summary>失败结果。</summary>
    public static InMemoryResponse Fail(int statusCode, string error) => new(statusCode, null, error);
}

/// <summary>
/// 进程内 MES 服务器：以内存字典/列表模拟 MES 后端，供单元测试与界面演示使用。
/// 既可预置查询数据，又可捕获所有上报报文以便断言。用户还可注册自定义处理器。
/// </summary>
public sealed class InMemoryMesServer
{
    private readonly IMesSerializer _serializer;
    private readonly ConcurrentDictionary<string, Func<TransportRequest, InMemoryResponse>> _handlers
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Func<TransportMessage, CancellationToken, Task>>> _subscribers
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>构造。</summary>
    public InMemoryMesServer(IMesSerializer? serializer = null)
    {
        _serializer = serializer ?? new JsonMesSerializer();
        RegisterDefaults();
    }

    // ---- 预置数据存储 ----

    /// <summary>工单存储（按工单号索引）。</summary>
    public ConcurrentDictionary<string, WorkOrder> WorkOrders { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>生产单元存储（按 SN 索引）。</summary>
    public ConcurrentDictionary<string, ProductUnit> Units { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>配方存储（按配方 Id 索引）。</summary>
    public ConcurrentDictionary<string, Recipe> Recipes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>追溯记录存储（按 SN 索引）。</summary>
    public ConcurrentDictionary<string, TraceabilityRecord> Traceability { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>过站放行判定（按 "SN|工序" 索引；未命中默认放行）。</summary>
    public ConcurrentDictionary<string, bool> GateDecisions { get; } = new(StringComparer.OrdinalIgnoreCase);

    // ---- 上报捕获 ----

    /// <summary>已捕获的检测结果。</summary>
    public ConcurrentQueue<InspectionResult> ReportedInspections { get; } = new();

    /// <summary>已捕获的测量上报（原始 JSON）。</summary>
    public ConcurrentQueue<string> ReportedMeasurements { get; } = new();

    /// <summary>已捕获的设备状态。</summary>
    public ConcurrentQueue<DeviceStatus> ReportedDeviceStatuses { get; } = new();

    /// <summary>已捕获的报警。</summary>
    public ConcurrentQueue<Alarm> ReportedAlarms { get; } = new();

    /// <summary>已捕获的图像上传。</summary>
    public ConcurrentQueue<ImageUploadDto> UploadedImages { get; } = new();

    /// <summary>已捕获的发布消息。</summary>
    public ConcurrentQueue<TransportMessage> PublishedMessages { get; } = new();

    /// <summary>注册/覆盖一个操作处理器。</summary>
    public InMemoryMesServer OnRequest(string operationKey, Func<TransportRequest, InMemoryResponse> handler)
    {
        _handlers[operationKey] = handler ?? throw new ArgumentNullException(nameof(handler));
        return this;
    }

    /// <summary>处理一次请求。</summary>
    public InMemoryResponse Handle(TransportRequest request)
    {
        if (_handlers.TryGetValue(request.Channel, out var handler))
            return handler(request);
        return InMemoryResponse.Fail(404, $"未处理的操作 '{request.Channel}'。");
    }

    /// <summary>发布一条消息并路由到订阅者。</summary>
    public async Task PublishAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        PublishedMessages.Enqueue(message);
        if (_subscribers.TryGetValue(message.Channel, out var handlers))
        {
            Func<TransportMessage, CancellationToken, Task>[] snapshot;
            lock (handlers)
                snapshot = handlers.ToArray();
            foreach (var h in snapshot)
                await h(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>订阅一个频道，返回取消订阅的动作。</summary>
    public IDisposable Subscribe(string channel, Func<TransportMessage, CancellationToken, Task> handler)
    {
        var list = _subscribers.GetOrAdd(channel, _ => new List<Func<TransportMessage, CancellationToken, Task>>());
        lock (list)
            list.Add(handler);
        return new Unsubscriber(list, handler);
    }

    /// <summary>模拟服务器主动下发一条消息到订阅者（如报警推送）。</summary>
    public Task PushAsync(string channel, object payload, CancellationToken cancellationToken = default)
    {
        var msg = new TransportMessage
        {
            Channel = channel,
            Body = _serializer.Serialize(payload),
            ContentType = _serializer.ContentType
        };
        return PublishAsync(msg, cancellationToken);
    }

    private void RegisterDefaults()
    {
        OnRequest(MesOperationKeys.GetWorkOrder, req =>
            TryParam(req, "workOrderId", out var id) && WorkOrders.TryGetValue(id, out var wo)
                ? InMemoryResponse.Ok(wo)
                : InMemoryResponse.Fail(404, "工单不存在。"));

        OnRequest(MesOperationKeys.GetUnit, req =>
            TryParam(req, "serialNumber", out var sn) && Units.TryGetValue(sn, out var u)
                ? InMemoryResponse.Ok(u)
                : InMemoryResponse.Fail(404, "单元不存在。"));

        OnRequest(MesOperationKeys.GetRecipe, req =>
            TryParam(req, "recipeId", out var id) && Recipes.TryGetValue(id, out var r)
                ? InMemoryResponse.Ok(r)
                : InMemoryResponse.Fail(404, "配方不存在。"));

        OnRequest(MesOperationKeys.GetTraceability, req =>
            TryParam(req, "serialNumber", out var sn) && Traceability.TryGetValue(sn, out var t)
                ? InMemoryResponse.Ok(t)
                : InMemoryResponse.Fail(404, "追溯记录不存在。"));

        OnRequest(MesOperationKeys.CheckUnitPassed, req =>
        {
            TryParam(req, "serialNumber", out var sn);
            TryParam(req, "operationId", out var op);
            var passed = !GateDecisions.TryGetValue($"{sn}|{op}", out var d) || d;
            return InMemoryResponse.Ok(new GateCheckResult { Passed = passed, Reason = passed ? "放行" : "拦截" });
        });

        OnRequest(MesOperationKeys.ReportInspection, req =>
        {
            var result = Read<InspectionResult>(req);
            if (result is not null)
                ReportedInspections.Enqueue(result);
            return InMemoryResponse.Ok(new { accepted = true, id = result?.Id });
        });

        OnRequest(MesOperationKeys.ReportMeasurements, req =>
        {
            if (req.Body is { Length: > 0 })
                ReportedMeasurements.Enqueue(System.Text.Encoding.UTF8.GetString(req.Body));
            return InMemoryResponse.Ok(new { accepted = true });
        });

        OnRequest(MesOperationKeys.ReportDeviceStatus, req =>
        {
            var status = Read<DeviceStatus>(req);
            if (status is not null)
                ReportedDeviceStatuses.Enqueue(status);
            return InMemoryResponse.Ok(new { accepted = true });
        });

        OnRequest(MesOperationKeys.RaiseAlarm, req =>
        {
            var alarm = Read<Alarm>(req);
            if (alarm is not null)
                ReportedAlarms.Enqueue(alarm);
            return InMemoryResponse.Ok(new { accepted = true, id = alarm?.Id });
        });

        OnRequest(MesOperationKeys.ClearAlarm, _ => InMemoryResponse.Ok(new { accepted = true }));

        OnRequest(MesOperationKeys.UploadImage, req =>
        {
            var dto = Read<ImageUploadDto>(req);
            if (dto is not null)
            {
                UploadedImages.Enqueue(dto);
                return InMemoryResponse.Ok(new ImageUploadReceipt
                {
                    ImageId = dto.Id,
                    StorageKey = $"mem://images/{dto.Id}",
                    RemoteUri = $"mem://images/{dto.Id}",
                    Size = dto.DataBase64 is null ? 0 : dto.DataBase64.Length
                });
            }
            return InMemoryResponse.Ok(new ImageUploadReceipt { ImageId = Guid.NewGuid().ToString("N") });
        });
    }

    private static bool TryParam(TransportRequest req, string key, out string value)
    {
        if (req.Parameters.TryGetValue(key, out var v) && v is not null)
        {
            value = v.ToString() ?? string.Empty;
            return !string.IsNullOrEmpty(value);
        }
        value = string.Empty;
        return false;
    }

    private T? Read<T>(TransportRequest req) where T : class
    {
        if (req.PayloadObject is T typed)
            return typed;
        if (req.Body is { Length: > 0 })
            return _serializer.Deserialize<T>(req.Body);
        return null;
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly List<Func<TransportMessage, CancellationToken, Task>> _list;
        private readonly Func<TransportMessage, CancellationToken, Task> _handler;

        public Unsubscriber(List<Func<TransportMessage, CancellationToken, Task>> list, Func<TransportMessage, CancellationToken, Task> handler)
        {
            _list = list;
            _handler = handler;
        }

        public void Dispose()
        {
            lock (_list)
                _list.Remove(_handler);
        }
    }
}
