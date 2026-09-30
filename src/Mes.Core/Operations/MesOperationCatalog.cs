using Mes.Core.Enums;
using Mes.Core.Transport;

namespace Mes.Core.Operations;

/// <summary>
/// 操作目录：管理操作键到 <see cref="MesOperationBinding"/> 的映射，
/// 并据此把高层调用解析为 <see cref="TransportRequest"/> / <see cref="TransportMessage"/>。
/// </summary>
public sealed class MesOperationCatalog
{
    private readonly Dictionary<string, MesOperationBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已注册的绑定数量。</summary>
    public int Count => _bindings.Count;

    /// <summary>所有已注册的操作键。</summary>
    public IReadOnlyCollection<string> Keys => _bindings.Keys;

    /// <summary>添加或覆盖一个绑定。</summary>
    public MesOperationCatalog Map(MesOperationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrWhiteSpace(binding.OperationKey))
            throw new ArgumentException("OperationKey 不能为空。", nameof(binding));
        _bindings[binding.OperationKey] = binding;
        return this;
    }

    /// <summary>以简化参数添加或覆盖一个绑定。</summary>
    public MesOperationCatalog Map(
        string operationKey,
        string channelTemplate,
        string? verb = null,
        bool requestResponse = true,
        MesPayloadFormat format = MesPayloadFormat.Json,
        int qos = 0,
        bool retain = false)
        => Map(new MesOperationBinding
        {
            OperationKey = operationKey,
            ChannelTemplate = channelTemplate,
            Verb = verb,
            RequestResponse = requestResponse,
            Format = format,
            Qos = qos,
            Retain = retain
        });

    /// <summary>获取绑定。</summary>
    public bool TryGet(string operationKey, out MesOperationBinding binding)
        => _bindings.TryGetValue(operationKey, out binding!);

    /// <summary>是否包含指定操作键。</summary>
    public bool Contains(string operationKey) => _bindings.ContainsKey(operationKey);

    /// <summary>移除绑定。</summary>
    public bool Remove(string operationKey) => _bindings.Remove(operationKey);

    /// <summary>
    /// 将操作解析为传输请求。占位符由 <paramref name="args"/> 填充。
    /// </summary>
    public TransportRequest BuildRequest(string operationKey, object? payload, IReadOnlyDictionary<string, object?> args)
    {
        if (!TryGet(operationKey, out var b))
            throw new KeyNotFoundException($"操作 '{operationKey}' 未在操作目录中映射。");

        var request = new TransportRequest
        {
            Channel = TemplateEngine.Render(b.ChannelTemplate, args),
            Verb = b.Verb,
            Format = b.Format,
            PayloadObject = payload
        };

        foreach (var kv in b.DefaultHeaders)
            request.Headers[kv.Key] = kv.Value;
        foreach (var kv in b.DefaultParameters)
            request.Parameters[kv.Key] = kv.Value;

        // 未用于通道模板的 args 作为参数传递（便于查询类操作）
        var tokens = TemplateEngine.ExtractTokens(b.ChannelTemplate);
        foreach (var kv in args)
        {
            if (!tokens.Contains(kv.Key) && !request.Parameters.ContainsKey(kv.Key))
                request.Parameters[kv.Key] = kv.Value;
        }

        return request;
    }

    /// <summary>
    /// 将操作解析为传输消息（发布语义）。
    /// </summary>
    public TransportMessage BuildMessage(string operationKey, byte[]? body, IReadOnlyDictionary<string, object?> args)
    {
        if (!TryGet(operationKey, out var b))
            throw new KeyNotFoundException($"操作 '{operationKey}' 未在操作目录中映射。");

        var msg = new TransportMessage
        {
            Channel = TemplateEngine.Render(b.ChannelTemplate, args),
            Body = body,
            Qos = b.Qos,
            Retain = b.Retain
        };
        foreach (var kv in b.DefaultHeaders)
            msg.Headers[kv.Key] = kv.Value;
        return msg;
    }

    /// <summary>克隆一份目录（用于每个客户端独立覆盖）。</summary>
    public MesOperationCatalog Clone()
    {
        var clone = new MesOperationCatalog();
        foreach (var b in _bindings.Values)
            clone.Map(b);
        return clone;
    }

    /// <summary>
    /// 创建一套 REST 默认端点映射，覆盖全部内置操作键。可按需覆盖。
    /// </summary>
    public static MesOperationCatalog CreateRestDefaults()
    {
        var c = new MesOperationCatalog();
        c.Map(MesOperationKeys.GetWorkOrder, "/api/mes/workorders/{workOrderId:url}", "GET");
        c.Map(MesOperationKeys.GetUnit, "/api/mes/units/{serialNumber:url}", "GET");
        c.Map(MesOperationKeys.ReportInspection, "/api/mes/inspections", "POST");
        c.Map(MesOperationKeys.ReportMeasurements, "/api/mes/units/{serialNumber:url}/measurements", "POST");
        c.Map(MesOperationKeys.UploadImage, "/api/mes/images", "POST");
        c.Map(MesOperationKeys.ReportDeviceStatus, "/api/mes/devices/{deviceId:url}/status", "POST");
        c.Map(MesOperationKeys.RaiseAlarm, "/api/mes/alarms", "POST");
        c.Map(MesOperationKeys.ClearAlarm, "/api/mes/alarms/{alarmCode:url}/clear", "POST");
        c.Map(MesOperationKeys.GetRecipe, "/api/mes/recipes/{recipeId:url}", "GET");
        c.Map(MesOperationKeys.GetTraceability, "/api/mes/traceability/{serialNumber:url}", "GET");
        c.Map(MesOperationKeys.CheckUnitPassed, "/api/mes/units/{serialNumber:url}/gate/{operationId:url}", "GET");
        return c;
    }

    /// <summary>
    /// 创建一套 MQTT 默认主题映射：上报类为发布语义（QoS 1）；查询类基于 MQTT 5
    /// “响应主题 + 关联数据”走请求/响应（发布到查询主题、在专属响应主题上等待回复）。
    /// </summary>
    public static MesOperationCatalog CreateMqttDefaults(string prefix = "mes")
    {
        var c = new MesOperationCatalog();
        // 上报类：单向发布。
        c.Map(MesOperationKeys.ReportInspection, $"{prefix}/inspection/{{serialNumber}}", requestResponse: false, qos: 1);
        c.Map(MesOperationKeys.ReportMeasurements, $"{prefix}/measurement/{{serialNumber}}", requestResponse: false, qos: 1);
        c.Map(MesOperationKeys.ReportDeviceStatus, $"{prefix}/device/{{deviceId}}/status", requestResponse: false, qos: 1);
        c.Map(MesOperationKeys.RaiseAlarm, $"{prefix}/alarm", requestResponse: false, qos: 1);
        c.Map(MesOperationKeys.ClearAlarm, $"{prefix}/alarm/clear", requestResponse: false, qos: 1);
        c.Map(MesOperationKeys.UploadImage, $"{prefix}/image/{{serialNumber}}", requestResponse: false, qos: 1);
        // 查询类：请求/响应（RPC）。
        c.Map(MesOperationKeys.GetWorkOrder, $"{prefix}/query/workorder/{{workOrderId}}", requestResponse: true, qos: 1);
        c.Map(MesOperationKeys.GetUnit, $"{prefix}/query/unit/{{serialNumber}}", requestResponse: true, qos: 1);
        c.Map(MesOperationKeys.GetRecipe, $"{prefix}/query/recipe/{{recipeId}}", requestResponse: true, qos: 1);
        c.Map(MesOperationKeys.GetTraceability, $"{prefix}/query/traceability/{{serialNumber}}", requestResponse: true, qos: 1);
        c.Map(MesOperationKeys.CheckUnitPassed, $"{prefix}/query/gate/{{serialNumber}}/{{operationId}}", requestResponse: true, qos: 1);
        return c;
    }

    /// <summary>
    /// 创建一套 SOAP/WCF 默认映射：以 SOAPAction/操作名作为通道；查询类走请求/响应，
    /// 上报类同样通过一次 SOAP 调用（大多数 SOAP MES 无单向语义）。
    /// </summary>
    public static MesOperationCatalog CreateSoapDefaults()
    {
        var c = new MesOperationCatalog();
        c.Map(MesOperationKeys.GetWorkOrder, "GetWorkOrder", "POST");
        c.Map(MesOperationKeys.GetUnit, "GetUnit", "POST");
        c.Map(MesOperationKeys.GetRecipe, "GetRecipe", "POST");
        c.Map(MesOperationKeys.GetTraceability, "GetTraceability", "POST");
        c.Map(MesOperationKeys.CheckUnitPassed, "CheckUnitPassed", "POST");
        c.Map(MesOperationKeys.ReportInspection, "ReportInspection", "POST");
        c.Map(MesOperationKeys.ReportMeasurements, "ReportMeasurements", "POST");
        c.Map(MesOperationKeys.UploadImage, "UploadImage", "POST");
        c.Map(MesOperationKeys.ReportDeviceStatus, "ReportDeviceStatus", "POST");
        c.Map(MesOperationKeys.RaiseAlarm, "RaiseAlarm", "POST");
        c.Map(MesOperationKeys.ClearAlarm, "ClearAlarm", "POST");
        return c;
    }

    /// <summary>
    /// 创建一套 Kafka 默认主题映射：上报类为发布语义；查询类基于“回复主题 + 关联标识”走请求/响应。
    /// </summary>
    public static MesOperationCatalog CreateKafkaDefaults(string prefix = "mes")
    {
        var c = new MesOperationCatalog();
        c.Map(MesOperationKeys.ReportInspection, $"{prefix}.inspection", requestResponse: false);
        c.Map(MesOperationKeys.ReportMeasurements, $"{prefix}.measurement", requestResponse: false);
        c.Map(MesOperationKeys.ReportDeviceStatus, $"{prefix}.device.status", requestResponse: false);
        c.Map(MesOperationKeys.RaiseAlarm, $"{prefix}.alarm", requestResponse: false);
        c.Map(MesOperationKeys.ClearAlarm, $"{prefix}.alarm.clear", requestResponse: false);
        c.Map(MesOperationKeys.UploadImage, $"{prefix}.image", requestResponse: false);
        c.Map(MesOperationKeys.GetWorkOrder, $"{prefix}.query.workorder", requestResponse: true);
        c.Map(MesOperationKeys.GetUnit, $"{prefix}.query.unit", requestResponse: true);
        c.Map(MesOperationKeys.GetRecipe, $"{prefix}.query.recipe", requestResponse: true);
        c.Map(MesOperationKeys.GetTraceability, $"{prefix}.query.traceability", requestResponse: true);
        c.Map(MesOperationKeys.CheckUnitPassed, $"{prefix}.query.gate", requestResponse: true);
        return c;
    }

    /// <summary>
    /// 创建一套 AMQP（RabbitMQ）默认路由键映射：上报类发布到主题交换机；查询类走 RPC（回复队列 + 关联标识）。
    /// </summary>
    public static MesOperationCatalog CreateAmqpDefaults(string prefix = "mes")
    {
        var c = new MesOperationCatalog();
        c.Map(MesOperationKeys.ReportInspection, $"{prefix}.inspection", requestResponse: false);
        c.Map(MesOperationKeys.ReportMeasurements, $"{prefix}.measurement", requestResponse: false);
        c.Map(MesOperationKeys.ReportDeviceStatus, $"{prefix}.device.status", requestResponse: false);
        c.Map(MesOperationKeys.RaiseAlarm, $"{prefix}.alarm", requestResponse: false);
        c.Map(MesOperationKeys.ClearAlarm, $"{prefix}.alarm.clear", requestResponse: false);
        c.Map(MesOperationKeys.UploadImage, $"{prefix}.image", requestResponse: false);
        c.Map(MesOperationKeys.GetWorkOrder, $"{prefix}.query.workorder", requestResponse: true);
        c.Map(MesOperationKeys.GetUnit, $"{prefix}.query.unit", requestResponse: true);
        c.Map(MesOperationKeys.GetRecipe, $"{prefix}.query.recipe", requestResponse: true);
        c.Map(MesOperationKeys.GetTraceability, $"{prefix}.query.traceability", requestResponse: true);
        c.Map(MesOperationKeys.CheckUnitPassed, $"{prefix}.query.gate", requestResponse: true);
        return c;
    }

    /// <summary>
    /// 创建一套 WebSocket 默认通道映射：查询类走请求/响应（消息内 correlationId 关联）；上报类为发布。
    /// </summary>
    public static MesOperationCatalog CreateWebSocketDefaults(string prefix = "mes")
    {
        var c = new MesOperationCatalog();
        c.Map(MesOperationKeys.ReportInspection, $"{prefix}/inspection", requestResponse: false);
        c.Map(MesOperationKeys.ReportMeasurements, $"{prefix}/measurement", requestResponse: false);
        c.Map(MesOperationKeys.ReportDeviceStatus, $"{prefix}/device/status", requestResponse: false);
        c.Map(MesOperationKeys.RaiseAlarm, $"{prefix}/alarm", requestResponse: false);
        c.Map(MesOperationKeys.ClearAlarm, $"{prefix}/alarm/clear", requestResponse: false);
        c.Map(MesOperationKeys.UploadImage, $"{prefix}/image", requestResponse: false);
        c.Map(MesOperationKeys.GetWorkOrder, $"{prefix}/query/workorder", requestResponse: true);
        c.Map(MesOperationKeys.GetUnit, $"{prefix}/query/unit", requestResponse: true);
        c.Map(MesOperationKeys.GetRecipe, $"{prefix}/query/recipe", requestResponse: true);
        c.Map(MesOperationKeys.GetTraceability, $"{prefix}/query/traceability", requestResponse: true);
        c.Map(MesOperationKeys.CheckUnitPassed, $"{prefix}/query/gate", requestResponse: true);
        return c;
    }

    /// <summary>
    /// 创建一套 gRPC 默认方法映射：通道即逻辑方法名，经由通用 <c>MesGateway</c> 契约（Invoke/Subscribe）承载。
    /// </summary>
    public static MesOperationCatalog CreateGrpcDefaults()
    {
        var c = new MesOperationCatalog();
        c.Map(MesOperationKeys.GetWorkOrder, "GetWorkOrder", requestResponse: true);
        c.Map(MesOperationKeys.GetUnit, "GetUnit", requestResponse: true);
        c.Map(MesOperationKeys.GetRecipe, "GetRecipe", requestResponse: true);
        c.Map(MesOperationKeys.GetTraceability, "GetTraceability", requestResponse: true);
        c.Map(MesOperationKeys.CheckUnitPassed, "CheckUnitPassed", requestResponse: true);
        c.Map(MesOperationKeys.ReportInspection, "ReportInspection", requestResponse: false);
        c.Map(MesOperationKeys.ReportMeasurements, "ReportMeasurements", requestResponse: false);
        c.Map(MesOperationKeys.UploadImage, "UploadImage", requestResponse: false);
        c.Map(MesOperationKeys.ReportDeviceStatus, "ReportDeviceStatus", requestResponse: false);
        c.Map(MesOperationKeys.RaiseAlarm, "RaiseAlarm", requestResponse: false);
        c.Map(MesOperationKeys.ClearAlarm, "ClearAlarm", requestResponse: false);
        return c;
    }
}
