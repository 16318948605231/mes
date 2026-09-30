using System.Text;
using System.Text.Json;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Exceptions;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workstation.ServiceModel.Ua;
using Workstation.ServiceModel.Ua.Channels;

namespace Mes.Protocols.OpcUa;

/// <summary>
/// OPC UA 传输（基于 Workstation.UaClient）。通道模板承载节点标识：
/// <list type="bullet">
/// <item><c>Verb=read</c>：读取 <c>Channel</c>（NodeId，如 <c>ns=2;s=MES/WorkOrder/123</c>）的 Value 属性。</item>
/// <item><c>Verb=write</c>：把请求体（JSON 文本）写入 <c>Channel</c> 节点的 Value 属性。</item>
/// <item><c>Verb=call</c>：调用方法，<c>Channel</c> 形如 <c>对象NodeId|方法NodeId</c>，请求体作为字符串入参。</item>
/// </list>
/// 安全策略默认 <see cref="SecurityPolicyUris.None"/>；如需加密请通过属性 <c>SecurityPolicy</c> 指定
/// 并自行提供证书（本 Provider 面向最常见的匿名/用户名 + 明文/None 场景，保持简单）。
/// </summary>
public sealed class OpcUaTransport : MesTransportBase
{
    private readonly MesOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private ClientSessionChannel? _channel;

    /// <summary>构造。</summary>
    public OpcUaTransport(MesOptions options, ILoggerFactory? loggerFactory = null)
        : base(options?.Name ?? "OpcUa", loggerFactory?.CreateLogger<OpcUaTransport>())
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        if (string.IsNullOrWhiteSpace(_options.Endpoint.BaseAddress))
            throw new MesConfigurationException("OPC UA 协议需要设置 Endpoint.BaseAddress（opc.tcp://host:port）。");
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.OpcUa;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities => MesTransportCapabilities.Request;

    /// <inheritdoc />
    protected override async Task DoConnectAsync(CancellationToken cancellationToken)
    {
        var app = new ApplicationDescription
        {
            ApplicationName = new LocalizedText(_options.Name ?? "Mes.Client", "en"),
            ApplicationUri = $"urn:{Environment.MachineName}:Mes:Client",
            ApplicationType = ApplicationType.Client
        };

        IUserIdentity identity = string.IsNullOrEmpty(_options.Auth.Username)
            ? new AnonymousIdentity()
            : new UserNameIdentity(_options.Auth.Username!, _options.Auth.Password ?? string.Empty);

        var securityPolicy = ResolveSecurityPolicy(_options.GetProperty("SecurityPolicy"));

        _channel = new ClientSessionChannel(
            app,
            null,
            identity,
            _options.Endpoint.BaseAddress!,
            securityPolicy,
            _loggerFactory,
            new ClientSessionChannelOptions(),
            StackProfiles.TcpUascBinary);

        await _channel.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            try { await _channel.CloseAsync(cancellationToken).ConfigureAwait(false); }
            catch { /* 忽略关闭异常 */ }
            _channel = null;
        }
    }

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        if (_channel is null)
            throw new MesTransportException("OPC UA 通道未建立。");

        var verb = (request.Verb ?? "read").ToLowerInvariant();
        return verb switch
        {
            "read" => await ReadAsync(request, cancellationToken).ConfigureAwait(false),
            "write" => await WriteAsync(request, cancellationToken).ConfigureAwait(false),
            "call" => await CallAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new MesTransportException($"OPC UA 不支持的动词 '{verb}'（read/write/call）。")
        };
    }

    private async Task<TransportResponse> ReadAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var read = new ReadRequest
        {
            NodesToRead = new[]
            {
                new ReadValueId { NodeId = NodeId.Parse(request.Channel), AttributeId = AttributeIds.Value }
            }
        };
        var response = await _channel!.ReadAsync(read, cancellationToken).ConfigureAwait(false);
        var dv = response.Results is { Length: > 0 } ? response.Results[0] : null;
        if (dv?.Value is null)
            return new TransportResponse { Success = false, StatusCode = 404, ErrorMessage = "节点无值或不存在。" };

        var body = dv.Value is string s ? Encoding.UTF8.GetBytes(s) : JsonSerializer.SerializeToUtf8Bytes(dv.Value);
        return TransportResponse.Ok(body, 200, "application/json");
    }

    private async Task<TransportResponse> WriteAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var json = request.Body is null ? string.Empty : Encoding.UTF8.GetString(request.Body);
        var write = new WriteRequest
        {
            NodesToWrite = new[]
            {
                new WriteValue
                {
                    NodeId = NodeId.Parse(request.Channel),
                    AttributeId = AttributeIds.Value,
                    Value = new DataValue(new Variant(json))
                }
            }
        };
        var response = await _channel!.WriteAsync(write, cancellationToken).ConfigureAwait(false);
        var status = response.Results is { Length: > 0 } ? response.Results[0] : default;
        var payload = Encoding.UTF8.GetBytes($"{{\"status\":{status.Value}}}");
        return TransportResponse.Ok(payload, 200, "application/json");
    }

    private async Task<TransportResponse> CallAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var parts = request.Channel.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var objectId = parts.Length > 1 ? NodeId.Parse(parts[0]) : NodeId.Parse("ns=0;i=85"); // ObjectsFolder
        var methodId = NodeId.Parse(parts.Length > 1 ? parts[1] : parts[0]);

        var json = request.Body is null ? string.Empty : Encoding.UTF8.GetString(request.Body);
        var inputs = string.IsNullOrEmpty(json) ? Array.Empty<Variant>() : new[] { new Variant(json) };

        var call = new CallRequest
        {
            MethodsToCall = new[]
            {
                new CallMethodRequest { ObjectId = objectId, MethodId = methodId, InputArguments = inputs }
            }
        };
        var response = await _channel!.CallAsync(call, cancellationToken).ConfigureAwait(false);
        var result = response.Results is { Length: > 0 } ? response.Results[0] : null;
        var outputs = result?.OutputArguments?.Select(v => v.Value).ToArray() ?? Array.Empty<object?>();
        return TransportResponse.Ok(JsonSerializer.SerializeToUtf8Bytes(outputs), 200, "application/json");
    }

    private static string ResolveSecurityPolicy(string? name) => (name ?? "None").Replace("_", string.Empty).ToLowerInvariant() switch
    {
        "none" => SecurityPolicyUris.None,
        "basic256sha256" => SecurityPolicyUris.Basic256Sha256,
        "basic256" => SecurityPolicyUris.Basic256,
        "basic128rsa15" => SecurityPolicyUris.Basic128Rsa15,
        "aes128sha256rsaoaep" => SecurityPolicyUris.Aes128_Sha256_RsaOaep,
        "aes256sha256rsapss" => SecurityPolicyUris.Aes256_Sha256_RsaPss,
        _ => SecurityPolicyUris.None
    };
}
