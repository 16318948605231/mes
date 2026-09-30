using Mes.Core.Enums;
using Mes.Core.Operations;

namespace Mes.Core.Configuration;

/// <summary>
/// MES 客户端总配置。可从 appsettings.json 绑定，或通过 Fluent 构建器/代码设置。
/// </summary>
public sealed class MesOptions
{
    /// <summary>客户端名称（多实例区分，默认 "default"）。</summary>
    public string Name { get; set; } = "default";

    /// <summary>协议种类。</summary>
    public MesProtocolKind Protocol { get; set; } = MesProtocolKind.Rest;

    /// <summary>端点配置。</summary>
    public MesEndpointOptions Endpoint { get; set; } = new();

    /// <summary>认证配置。</summary>
    public MesAuthOptions Auth { get; set; } = new();

    /// <summary>重试配置。</summary>
    public MesRetryOptions Retry { get; set; } = new();

    /// <summary>自动重连配置。</summary>
    public MesReconnectOptions Reconnect { get; set; } = new();

    /// <summary>TLS 配置。</summary>
    public MesTlsOptions Tls { get; set; } = new();

    /// <summary>图像配置。</summary>
    public MesImageOptions Image { get; set; } = new();

    /// <summary>默认请求超时（毫秒）。</summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>是否在首次调用时自动连接。</summary>
    public bool AutoConnect { get; set; } = true;

    /// <summary>默认请求头。</summary>
    public IDictionary<string, string> DefaultHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>默认查询参数。</summary>
    public IDictionary<string, object?> DefaultParameters { get; set; } = new Dictionary<string, object?>();

    /// <summary>
    /// 操作绑定覆盖列表。用于覆盖某个协议的默认操作映射，或新增自定义操作。
    /// </summary>
    public IList<MesOperationBinding> OperationBindings { get; set; } = new List<MesOperationBinding>();

    /// <summary>
    /// 协议特有属性（键值对）。例如 MQTT 的 ClientId/TopicPrefix，
    /// 数据库的 ProviderInvariantName，OPC UA 的节点映射等。
    /// </summary>
    public IDictionary<string, string?> Properties { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>读取协议特有属性。</summary>
    public string? GetProperty(string key, string? defaultValue = null)
        => Properties.TryGetValue(key, out var v) && v is not null ? v : defaultValue;

    /// <summary>设置协议特有属性。</summary>
    public MesOptions SetProperty(string key, string? value)
    {
        Properties[key] = value;
        return this;
    }

    /// <summary>基础校验；返回错误信息列表（空表示通过）。</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Name 不能为空。");

        switch (Protocol)
        {
            case MesProtocolKind.Rest:
                if (string.IsNullOrWhiteSpace(Endpoint.BaseAddress))
                    errors.Add("REST 协议需要设置 Endpoint.BaseAddress。");
                break;
            case MesProtocolKind.Mqtt:
            case MesProtocolKind.SecsGem:
                if (string.IsNullOrWhiteSpace(Endpoint.Host))
                    errors.Add($"{Protocol} 协议需要设置 Endpoint.Host。");
                break;
            case MesProtocolKind.OpcUa:
                if (string.IsNullOrWhiteSpace(Endpoint.BaseAddress))
                    errors.Add("OPC UA 协议需要设置 Endpoint.BaseAddress（opc.tcp://...）。");
                break;
            case MesProtocolKind.Database:
                if (string.IsNullOrWhiteSpace(Endpoint.BaseAddress))
                    errors.Add("数据库协议需要设置 Endpoint.BaseAddress（连接字符串）。");
                break;
            case MesProtocolKind.FileDrop:
                if (string.IsNullOrWhiteSpace(Endpoint.BaseAddress))
                    errors.Add("文件落地协议需要设置 Endpoint.BaseAddress（交换根目录）。");
                break;
        }

        if (TimeoutMs <= 0)
            errors.Add("TimeoutMs 必须为正数。");

        return errors;
    }
}
