using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Soap;

/// <summary>
/// SOAP / WCF 传输：把 <see cref="TransportRequest"/> 封装成 SOAP 信封并 POST 到服务端点。
/// 面向“JSON-over-SOAP”约定或 MES 提供的通用 SOAP 网关：
/// 请求体（JSON）作为操作元素的文本内容发送；响应从 <c>soap:Body</c> 的首个子元素中取回内层文本/XML。
/// 支持 SOAP 1.1（<c>text/xml</c> + <c>SOAPAction</c> 头）与 SOAP 1.2（<c>application/soap+xml</c> + action 参数）。
/// </summary>
public sealed class SoapTransport : MesTransportBase
{
    private const string Soap11Ns = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string Soap12Ns = "http://www.w3.org/2003/05/soap-envelope";

    private readonly MesOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _serviceNamespace;
    private readonly bool _soap12;

    /// <summary>构造。传入自定义 <see cref="HttpClient"/> 可用于测试。</summary>
    public SoapTransport(MesOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base(options?.Name ?? "Soap", logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serviceNamespace = _options.GetProperty("Namespace") ?? "http://mes.local/";
        _soap12 = string.Equals(_options.GetProperty("SoapVersion"), "1.2", StringComparison.Ordinal);

        if (httpClient is null)
        {
            var handler = new HttpClientHandler();
            ConfigureTls(handler);
            _http = new HttpClient(handler);
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
        }

        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.Endpoint.BaseAddress))
            _http.BaseAddress = new Uri(_options.Endpoint.BaseAddress, UriKind.Absolute);

        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.Soap;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities => MesTransportCapabilities.Request;

    /// <inheritdoc />
    protected override Task DoConnectAsync(CancellationToken cancellationToken)
    {
        if (_http.BaseAddress is null)
            throw new Mes.Core.Exceptions.MesConfigurationException("SOAP 传输需要设置 Endpoint.BaseAddress（服务端点 URL）。");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task DoDisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var operation = string.IsNullOrWhiteSpace(request.Channel) ? "Invoke" : request.Channel;
        var payloadText = request.Body is { Length: > 0 } ? Encoding.UTF8.GetString(request.Body) : string.Empty;
        var envelope = BuildEnvelope(operation, payloadText);
        var soapAction = $"{_serviceNamespace.TrimEnd('/')}/{operation}";

        var contentType = _soap12 ? $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"" : "text/xml; charset=utf-8";
        using var content = new StringContent(envelope, new UTF8Encoding(false));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        using var message = new HttpRequestMessage(HttpMethod.Post, string.Empty) { Content = content };
        if (!_soap12)
            message.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");
        foreach (var kv in request.Headers)
            message.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        ApplyAuth(message);

        using var timeoutCts = request.TimeoutMs is > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutCts?.CancelAfter(request.TimeoutMs!.Value);
        var token = timeoutCts?.Token ?? cancellationToken;

        using var httpResponse = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
        var raw = await httpResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);

        var inner = TryExtractBodyInner(raw, out var fault);
        var success = httpResponse.IsSuccessStatusCode && fault is null;

        return new TransportResponse
        {
            Success = success,
            StatusCode = (int)httpResponse.StatusCode,
            Body = inner is null ? null : Encoding.UTF8.GetBytes(inner),
            ContentType = "application/json",
            CorrelationId = request.CorrelationId,
            ErrorMessage = success ? null : fault ?? $"HTTP {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}"
        };
    }

    private string BuildEnvelope(string operation, string payloadText)
    {
        var ns = _soap12 ? Soap12Ns : Soap11Ns;
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<soap:Envelope xmlns:soap=\"").Append(ns).Append("\">");
        sb.Append("<soap:Body>");
        sb.Append('<').Append(operation).Append(" xmlns=\"").Append(System.Security.SecurityElement.Escape(_serviceNamespace)).Append("\">");
        if (!string.IsNullOrEmpty(payloadText))
            sb.Append("<![CDATA[").Append(payloadText).Append("]]>");
        sb.Append("</").Append(operation).Append('>');
        sb.Append("</soap:Body>");
        sb.Append("</soap:Envelope>");
        return sb.ToString();
    }

    private static string? TryExtractBodyInner(string xml, out string? fault)
    {
        fault = null;
        if (string.IsNullOrWhiteSpace(xml))
            return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("s11", Soap11Ns);
            nsmgr.AddNamespace("s12", Soap12Ns);

            var body = doc.SelectSingleNode("//s11:Body", nsmgr) ?? doc.SelectSingleNode("//s12:Body", nsmgr);
            if (body is null)
                return xml;

            var faultNode = doc.SelectSingleNode("//s11:Fault", nsmgr) ?? doc.SelectSingleNode("//s12:Fault", nsmgr);
            if (faultNode is not null)
            {
                fault = faultNode.InnerText;
                return null;
            }

            XmlNode? firstElement = null;
            foreach (XmlNode child in body.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                {
                    firstElement = child;
                    break;
                }
            }
            var content = firstElement ?? body;
            var text = content.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
                return text;
            return content.HasChildNodes ? content.InnerXml : null;
        }
        catch (XmlException)
        {
            // 非 XML 响应（可能是纯 JSON），原样返回。
            return xml;
        }
    }

    private void ApplyAuth(HttpRequestMessage message)
    {
        var auth = _options.Auth;
        switch (auth.Type)
        {
            case MesAuthType.Basic:
            case MesAuthType.UsernamePassword:
                var raw = Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Password}");
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
                break;
            case MesAuthType.BearerToken:
                if (!string.IsNullOrEmpty(auth.Token))
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
                break;
            case MesAuthType.ApiKey:
                if (!string.IsNullOrEmpty(auth.ApiKey))
                    message.Headers.TryAddWithoutValidation(auth.ApiKeyHeader, auth.ApiKey);
                break;
        }
    }

    private void ConfigureTls(HttpClientHandler handler)
    {
        var tls = _options.Tls;
        if (tls.AllowUntrustedCertificates)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        var certPath = tls.ClientCertificatePath ?? (_options.Auth.Type == MesAuthType.Certificate ? _options.Auth.CertificatePath : null);
        if (!string.IsNullOrWhiteSpace(certPath) && File.Exists(certPath))
        {
            var pwd = tls.ClientCertificatePassword ?? _options.Auth.CertificatePassword;
#if NET9_0_OR_GREATER
            handler.ClientCertificates.Add(X509CertificateLoader.LoadPkcs12FromFile(certPath, pwd));
#else
            handler.ClientCertificates.Add(new X509Certificate2(certPath, pwd));
#endif
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (_ownsHttp)
            _http.Dispose();
    }
}
