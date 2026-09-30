using System.Diagnostics;
using System.Security.Cryptography;
using Mes.Core.Common;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Events;
using Mes.Core.Models;
using Mes.Core.Operations;
using Mes.Core.Resilience;
using Mes.Core.Serialization;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mes.Core.Client;

/// <summary>
/// <see cref="IMesClient"/> 的默认实现：将高层业务操作映射到协议无关的传输调用。
/// </summary>
public sealed class MesClient : IMesClient
{
    private readonly IMesTransport _transport;
    private readonly MesOperationCatalog _catalog;
    private readonly IMesSerializer _serializer;
    private readonly MesOptions _options;
    private readonly ILogger _logger;
    private readonly RetryExecutor _retry;
    private bool _disposed;

    /// <summary>构造。</summary>
    public MesClient(
        IMesTransport transport,
        MesOperationCatalog catalog,
        IMesSerializer? serializer = null,
        MesOptions? options = null,
        ILogger<MesClient>? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _serializer = serializer ?? new JsonMesSerializer();
        _options = options ?? new MesOptions();
        _logger = logger ?? NullLogger<MesClient>.Instance;
        _retry = new RetryExecutor(_options.Retry, _logger);

        _transport.StateChanged += OnTransportStateChanged;
        _transport.Error += OnTransportError;
    }

    /// <inheritdoc />
    public string Name => _options.Name;

    /// <inheritdoc />
    public MesConnectionState State => _transport.State;

    /// <inheritdoc />
    public IMesTransport Transport => _transport;

    /// <inheritdoc />
    public event EventHandler<MesConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <inheritdoc />
    public event EventHandler<MesMessageReceivedEventArgs>? MessageReceived;

    /// <inheritdoc />
    public event EventHandler<MesAlarmEventArgs>? AlarmReceived;

    /// <inheritdoc />
    public event EventHandler<MesInspectionReportedEventArgs>? InspectionReported;

    /// <inheritdoc />
    public event EventHandler<MesErrorEventArgs>? ErrorOccurred;

    private void OnTransportStateChanged(object? sender, MesConnectionStateChangedEventArgs e)
        => ConnectionStateChanged?.Invoke(this, e);

    private void OnTransportError(object? sender, MesErrorEventArgs e)
        => ErrorOccurred?.Invoke(this, e);

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
        => _transport.ConnectAsync(cancellationToken);

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _transport.DisconnectAsync(cancellationToken);

    // ---------------- 查询类 ----------------

    /// <inheritdoc />
    public Task<MesResult<WorkOrder>> GetWorkOrderAsync(string workOrderId, CancellationToken cancellationToken = default)
        => InvokeAsync<WorkOrder>(MesOperationKeys.GetWorkOrder, null, Args(("workOrderId", workOrderId)), cancellationToken);

    /// <inheritdoc />
    public Task<MesResult<ProductUnit>> GetUnitAsync(string serialNumber, CancellationToken cancellationToken = default)
        => InvokeAsync<ProductUnit>(MesOperationKeys.GetUnit, null, Args(("serialNumber", serialNumber)), cancellationToken);

    /// <inheritdoc />
    public Task<MesResult<Recipe>> GetRecipeAsync(string recipeId, CancellationToken cancellationToken = default)
        => InvokeAsync<Recipe>(MesOperationKeys.GetRecipe, null, Args(("recipeId", recipeId)), cancellationToken);

    /// <inheritdoc />
    public Task<MesResult<TraceabilityRecord>> GetTraceabilityAsync(string serialNumber, CancellationToken cancellationToken = default)
        => InvokeAsync<TraceabilityRecord>(MesOperationKeys.GetTraceability, null, Args(("serialNumber", serialNumber)), cancellationToken);

    /// <inheritdoc />
    public async Task<MesResult<bool>> CheckUnitPassedAsync(string serialNumber, string operationId, CancellationToken cancellationToken = default)
    {
        var res = await InvokeAsync<GateCheckResult>(
            MesOperationKeys.CheckUnitPassed, null,
            Args(("serialNumber", serialNumber), ("operationId", operationId)), cancellationToken).ConfigureAwait(false);

        if (res.Success)
            return MesResult<bool>.Ok(res.Value?.Passed ?? true, res.Message, res.CorrelationId);
        return MesResult<bool>.Fail(res.Code, res.Message, res.Exception, res.CorrelationId);
    }

    // ---------------- 上报类 ----------------

    /// <inheritdoc />
    public async Task<MesResult> ReportInspectionResultAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            await ProcessImagesAsync(result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理检测结果图像时出错。");
        }

        var args = Args(
            ("serialNumber", result.SerialNumber),
            ("workOrderId", result.WorkOrderId),
            ("productCode", result.ProductCode),
            ("stationId", result.StationId),
            ("operationId", result.OperationId));

        var r = await InvokeAsync(MesOperationKeys.ReportInspection, result, args, cancellationToken).ConfigureAwait(false);
        try
        {
            InspectionReported?.Invoke(this, new MesInspectionReportedEventArgs(result, r.Success));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InspectionReported 事件处理器抛出异常。");
        }
        return r;
    }

    /// <inheritdoc />
    public Task<MesResult> ReportMeasurementsAsync(string serialNumber, IEnumerable<Measurement> measurements, CancellationToken cancellationToken = default)
    {
        var payload = new { serialNumber, measurements = measurements.ToList() };
        return InvokeAsync(MesOperationKeys.ReportMeasurements, payload, Args(("serialNumber", serialNumber)), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MesResult<ImageUploadReceipt>> UploadImageAsync(MesImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        await PrepareImageAsync(image, cancellationToken).ConfigureAwait(false);

        var dto = new ImageUploadDto
        {
            Id = image.Id,
            FileName = image.FileName,
            ContentType = image.ContentType,
            Format = image.Format,
            Width = image.Width,
            Height = image.Height,
            Sha256 = image.Sha256,
            DataBase64 = image.Data is null ? null : Convert.ToBase64String(image.Data)
        };

        var args = Args(("imageId", image.Id), ("fileName", image.FileName), ("serialNumber", image.Attributes.TryGetValue("serialNumber", out var sn) ? sn : null));

        if (!_catalog.TryGet(_options.Image.UploadOperationKey, out var binding))
            return MesResult<ImageUploadReceipt>.Fail(MesResultCodes.OperationNotMapped, $"图像上传操作 '{_options.Image.UploadOperationKey}' 未映射。");

        if (binding.RequestResponse)
        {
            var res = await InvokeAsync<ImageUploadReceipt>(_options.Image.UploadOperationKey, dto, args, cancellationToken).ConfigureAwait(false);
            if (res.Success && res.Value is not null)
            {
                res.Value.ImageId = image.Id;
                image.RemoteUri = res.Value.RemoteUri;
                image.StorageKey = res.Value.StorageKey;
            }
            return res;
        }

        // 发布语义（如 MQTT）：无回执，构造合成回执
        var pub = await InvokeAsync(_options.Image.UploadOperationKey, dto, args, cancellationToken).ConfigureAwait(false);
        if (!pub.Success)
            return MesResult<ImageUploadReceipt>.Fail(pub.Code, pub.Message, pub.Exception, pub.CorrelationId);

        return MesResult<ImageUploadReceipt>.Ok(new ImageUploadReceipt
        {
            ImageId = image.Id,
            StorageKey = image.StorageKey,
            RemoteUri = image.RemoteUri,
            Size = image.Length ?? 0
        }, pub.Message, pub.CorrelationId);
    }

    /// <inheritdoc />
    public Task<MesResult> ReportDeviceStatusAsync(DeviceStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        return InvokeAsync(MesOperationKeys.ReportDeviceStatus, status, Args(("deviceId", status.DeviceId)), cancellationToken);
    }

    /// <inheritdoc />
    public Task<MesResult> RaiseAlarmAsync(Alarm alarm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alarm);
        return InvokeAsync(MesOperationKeys.RaiseAlarm, alarm, Args(("deviceId", alarm.DeviceId), ("alarmCode", alarm.Code)), cancellationToken);
    }

    /// <inheritdoc />
    public Task<MesResult> ClearAlarmAsync(string alarmCode, CancellationToken cancellationToken = default)
        => InvokeAsync(MesOperationKeys.ClearAlarm, new { alarmCode }, Args(("alarmCode", alarmCode)), cancellationToken);

    // ---------------- 通用出口 ----------------

    /// <inheritdoc />
    public async Task<MesResult<TResponse>> InvokeAsync<TResponse>(string operationKey, object? payload = null, IDictionary<string, object?>? args = null, CancellationToken cancellationToken = default)
    {
        if (!_catalog.TryGet(operationKey, out var binding))
            return MesResult<TResponse>.Fail(MesResultCodes.OperationNotMapped, $"操作 '{operationKey}' 未映射。");

        var effectiveArgs = MergeArgs(args);

        if (!binding.RequestResponse)
        {
            var pub = await PublishInternalAsync(operationKey, payload, effectiveArgs, cancellationToken).ConfigureAwait(false);
            return pub.Success
                ? MesResult<TResponse>.Ok(default!, pub.Message, pub.CorrelationId)
                : MesResult<TResponse>.Fail(pub.Code, pub.Message, pub.Exception, pub.CorrelationId);
        }

        var response = await SendRequestAsync(operationKey, payload, effectiveArgs, cancellationToken).ConfigureAwait(false);
        return MapResponse<TResponse>(response);
    }

    /// <inheritdoc />
    public async Task<MesResult> InvokeAsync(string operationKey, object? payload = null, IDictionary<string, object?>? args = null, CancellationToken cancellationToken = default)
    {
        if (!_catalog.TryGet(operationKey, out var binding))
            return MesResult.Fail(MesResultCodes.OperationNotMapped, $"操作 '{operationKey}' 未映射。");

        var effectiveArgs = MergeArgs(args);

        if (!binding.RequestResponse)
            return await PublishInternalAsync(operationKey, payload, effectiveArgs, cancellationToken).ConfigureAwait(false);

        var response = await SendRequestAsync(operationKey, payload, effectiveArgs, cancellationToken).ConfigureAwait(false);
        return MapResponse(response);
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> SubscribeAsync<T>(string channel, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return await _transport.SubscribeAsync(channel, async (msg, c) =>
        {
            try
            {
                MessageReceived?.Invoke(this, new MesMessageReceivedEventArgs(channel, msg));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MessageReceived 事件处理器抛出异常。");
            }

            var obj = _serializer.Deserialize<T>(msg.Body);
            if (obj is not null)
                await handler(obj, c).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IAsyncDisposable> SubscribeAlarmsAsync(string channel, Func<Alarm, CancellationToken, Task>? handler = null, CancellationToken cancellationToken = default)
        => SubscribeAsync<Alarm>(channel, async (alarm, c) =>
        {
            try
            {
                AlarmReceived?.Invoke(this, new MesAlarmEventArgs(alarm));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AlarmReceived 事件处理器抛出异常。");
            }

            if (handler is not null)
                await handler(alarm, c).ConfigureAwait(false);
        }, cancellationToken);

    // ---------------- 内部 ----------------

    private async Task<TransportResponse> SendRequestAsync(string operationKey, object? payload, IReadOnlyDictionary<string, object?> args, CancellationToken cancellationToken)
    {
        var request = _catalog.BuildRequest(operationKey, payload, args);
        ApplyDefaults(request);

        if (payload is not null && request.Body is null)
        {
            request.Body = _serializer.Serialize(payload);
            request.ContentType ??= _serializer.ContentType;
        }

        if (_options.AutoConnect)
            await EnsureConnectedSafeAsync(cancellationToken).ConfigureAwait(false);

        return await _retry.ExecuteAsync(
            c => _transport.RequestAsync(request, c),
            r => ShouldRetry(r),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<MesResult> PublishInternalAsync(string operationKey, object? payload, IReadOnlyDictionary<string, object?> args, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var body = payload is null ? null : _serializer.Serialize(payload);
            var msg = _catalog.BuildMessage(operationKey, body, args);
            msg.ContentType ??= _serializer.ContentType;

            if (_options.AutoConnect)
                await EnsureConnectedSafeAsync(cancellationToken).ConfigureAwait(false);

            await _transport.PublishAsync(msg, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return new MesResult { Success = true, Code = MesResultCodes.Ok, CorrelationId = msg.CorrelationId, ElapsedMilliseconds = sw.ElapsedMilliseconds };
        }
        catch (OperationCanceledException)
        {
            return MesResult.Fail(MesResultCodes.Cancelled, "操作已取消。");
        }
        catch (Exception ex)
        {
            return MesResult.FromException(ex);
        }
    }

    private async Task EnsureConnectedSafeAsync(CancellationToken cancellationToken)
    {
        if (_transport.State != MesConnectionState.Connected)
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ApplyDefaults(TransportRequest request)
    {
        foreach (var kv in _options.DefaultHeaders)
            request.Headers.TryAdd(kv.Key, kv.Value);
        foreach (var kv in _options.DefaultParameters)
            request.Parameters.TryAdd(kv.Key, kv.Value);
        request.TimeoutMs ??= _options.TimeoutMs;
        request.Headers.TryAdd(MesConstants.CorrelationIdHeader, request.CorrelationId);
    }

    private MesResult<T> MapResponse<T>(TransportResponse response)
    {
        if (!response.Success)
            return new MesResult<T>
            {
                Success = false,
                Code = CodeFromStatus(response.StatusCode),
                Message = response.ErrorMessage ?? $"传输失败（状态码 {response.StatusCode}）。",
                CorrelationId = response.CorrelationId,
                ElapsedMilliseconds = response.ElapsedMilliseconds,
                Metadata = BuildMetadata(response)
            };

        T? value = default;
        if (response.Body is { Length: > 0 } && typeof(T) != typeof(object))
        {
            try { value = _serializer.Deserialize<T>(response.Body); }
            catch (Exception ex) { _logger.LogWarning(ex, "响应反序列化失败。"); }
        }

        return new MesResult<T>
        {
            Success = true,
            Code = MesResultCodes.Ok,
            Value = value,
            CorrelationId = response.CorrelationId,
            ElapsedMilliseconds = response.ElapsedMilliseconds,
            Metadata = BuildMetadata(response)
        };
    }

    private MesResult MapResponse(TransportResponse response)
    {
        if (!response.Success)
            return new MesResult { Success = false, Code = CodeFromStatus(response.StatusCode), Message = response.ErrorMessage ?? $"传输失败（状态码 {response.StatusCode}）。", CorrelationId = response.CorrelationId, ElapsedMilliseconds = response.ElapsedMilliseconds, Metadata = BuildMetadata(response) };

        return new MesResult { Success = true, Code = MesResultCodes.Ok, CorrelationId = response.CorrelationId, ElapsedMilliseconds = response.ElapsedMilliseconds, Metadata = BuildMetadata(response) };
    }

    private static IReadOnlyDictionary<string, object?> BuildMetadata(TransportResponse response)
    {
        var meta = new Dictionary<string, object?> { ["statusCode"] = response.StatusCode };
        foreach (var h in response.Headers)
            meta[$"header:{h.Key}"] = h.Value;
        return meta;
    }

    private static string CodeFromStatus(int status) => status switch
    {
        >= 200 and < 300 => MesResultCodes.Ok,
        400 => MesResultCodes.ValidationError,
        401 or 403 => MesResultCodes.Unauthorized,
        404 => MesResultCodes.NotFound,
        408 or 504 => MesResultCodes.Timeout,
        >= 500 => MesResultCodes.ServerError,
        _ => MesResultCodes.TransportError
    };

    private bool ShouldRetry(TransportResponse response)
    {
        if (response.Success)
            return false;
        return response.StatusCode is 0 or 408 or 425 or 429 or 500 or 502 or 503 or 504;
    }

    private async Task ProcessImagesAsync(InspectionResult result, CancellationToken cancellationToken)
    {
        foreach (var image in result.Images)
        {
            await PrepareImageAsync(image, cancellationToken).ConfigureAwait(false);
            var mode = ResolveTransferMode(image);

            if (mode == ImageTransferMode.SeparateUpload)
            {
                image.Attributes["serialNumber"] = result.SerialNumber;
                var up = await UploadImageAsync(image, cancellationToken).ConfigureAwait(false);
                if (up.Success)
                {
                    image.Data = null; // 已单独上传，避免随结果重复传输
                    image.TransferMode = ImageTransferMode.ReferenceOnly;
                }
            }
        }
    }

    private ImageTransferMode ResolveTransferMode(MesImage image)
    {
        var mode = image.TransferMode;
        if (mode == ImageTransferMode.Embedded
            && _options.Image.MaxEmbeddedBytes > 0
            && (image.Data?.LongLength ?? 0) > _options.Image.MaxEmbeddedBytes)
        {
            _logger.LogDebug("图像 {Id} 超过内嵌上限，改为单独上传。", image.Id);
            return ImageTransferMode.SeparateUpload;
        }
        return mode;
    }

    private async Task PrepareImageAsync(MesImage image, CancellationToken cancellationToken)
    {
        if (image.Data is null && !string.IsNullOrEmpty(image.LocalPath) && File.Exists(image.LocalPath))
            image.Data = await File.ReadAllBytesAsync(image.LocalPath, cancellationToken).ConfigureAwait(false);

        if (image.Format == ImageFormat.Unknown && !string.IsNullOrEmpty(image.FileName))
        {
            var (fmt, ct) = MesImage.InferFormat(image.FileName);
            image.Format = fmt;
            image.ContentType ??= ct;
        }

        if (_options.Image.ComputeSha256 && image.Data is not null && string.IsNullOrEmpty(image.Sha256))
            image.Sha256 = Convert.ToHexString(SHA256.HashData(image.Data)).ToLowerInvariant();
    }

    private IReadOnlyDictionary<string, object?> MergeArgs(IDictionary<string, object?>? args)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _options.DefaultParameters)
            dict[kv.Key] = kv.Value;
        if (args is not null)
            foreach (var kv in args)
                dict[kv.Key] = kv.Value;
        return dict;
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
            dict[k] = v;
        return dict;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        _transport.StateChanged -= OnTransportStateChanged;
        _transport.Error -= OnTransportError;
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
