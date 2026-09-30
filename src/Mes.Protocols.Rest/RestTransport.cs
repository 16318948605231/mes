using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Rest;

/// <summary>
/// REST/HTTP 传输：将 <see cref="TransportRequest"/> 映射为 HTTP 请求。
/// 支持 Basic / 持有者令牌 / API Key / OAuth2 客户端凭据认证与 TLS 配置。
/// </summary>
public sealed class RestTransport : MesTransportBase
{
    private readonly MesOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    /// <summary>构造。传入自定义 <see cref="HttpClient"/> 可用于测试（Mock Handler）。</summary>
    public RestTransport(MesOptions options, HttpClient? httpClient = null, ILogger? logger = null)
        : base(options?.Name ?? "Rest", logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

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

        _http.Timeout = Timeout.InfiniteTimeSpan; // 由每次请求的 CTS 控制超时
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.Rest;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities => MesTransportCapabilities.Request;

    /// <inheritdoc />
    protected override Task DoConnectAsync(CancellationToken cancellationToken)
    {
        if (_http.BaseAddress is null)
            throw new Mes.Core.Exceptions.MesConfigurationException("REST 传输需要设置 Endpoint.BaseAddress。");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task DoDisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        using var message = await BuildHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

        using var timeoutCts = request.TimeoutMs is > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutCts?.CancelAfter(request.TimeoutMs!.Value);
        var token = timeoutCts?.Token ?? cancellationToken;

        using var httpResponse = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
        var body = await httpResponse.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);

        var response = new TransportResponse
        {
            Success = httpResponse.IsSuccessStatusCode,
            StatusCode = (int)httpResponse.StatusCode,
            Body = body,
            ContentType = httpResponse.Content.Headers.ContentType?.ToString(),
            CorrelationId = request.CorrelationId,
            ErrorMessage = httpResponse.IsSuccessStatusCode ? null : $"HTTP {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}"
        };
        foreach (var h in httpResponse.Headers)
            response.Headers[h.Key] = string.Join(",", h.Value);
        return response;
    }

    private async Task<HttpRequestMessage> BuildHttpRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var method = new HttpMethod((request.Verb ?? (request.Body is null ? "GET" : "POST")).ToUpperInvariant());
        var url = BuildUrl(request, method);
        var message = new HttpRequestMessage(method, url);

        foreach (var kv in request.Headers)
            message.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        if (request.Body is { Length: > 0 } && method != HttpMethod.Get && method != HttpMethod.Head)
        {
            var content = new ByteArrayContent(request.Body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType ?? "application/json");
            message.Content = content;
        }

        await ApplyAuthAsync(message, cancellationToken).ConfigureAwait(false);
        return message;
    }

    private string BuildUrl(TransportRequest request, HttpMethod method)
    {
        var channel = request.Channel;
        // 仅在 GET/HEAD/DELETE 或无请求体时把参数拼到查询串
        var appendQuery = method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Delete || request.Body is null;
        if (!appendQuery || request.Parameters.Count == 0)
            return channel;

        var qs = new StringBuilder();
        foreach (var kv in request.Parameters)
        {
            if (kv.Value is null)
                continue;
            qs.Append(qs.Length == 0 ? '?' : '&');
            qs.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value.ToString() ?? string.Empty));
        }
        return channel + qs;
    }

    private async Task ApplyAuthAsync(HttpRequestMessage message, CancellationToken cancellationToken)
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
            case MesAuthType.OAuth2ClientCredentials:
                var token = await GetOAuthTokenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;
        }
    }

    private async Task<string?> GetOAuthTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _cachedToken;

        await _tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
                return _cachedToken;

            var auth = _options.Auth;
            if (string.IsNullOrWhiteSpace(auth.TokenUrl))
                throw new Mes.Core.Exceptions.MesConfigurationException("OAuth2 需要设置 Auth.TokenUrl。");

            var form = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "client_credentials"),
                new("client_id", auth.ClientId ?? string.Empty),
                new("client_secret", auth.ClientSecret ?? string.Empty)
            };
            if (!string.IsNullOrWhiteSpace(auth.Scope))
                form.Add(new("scope", auth.Scope!));

            using var req = new HttpRequestMessage(HttpMethod.Post, auth.TokenUrl)
            {
                Content = new FormUrlEncodedContent(form)
            };
            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            var expiresIn = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var s) ? s : 3600;

            _cachedToken = accessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 30));
            return _cachedToken;
        }
        finally
        {
            _tokenGate.Release();
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
            handler.ClientCertificates.Add(X509CertificateLoader.LoadPkcs12FromFile(certPath, pwd));
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _tokenGate.Dispose();
        if (_ownsHttp)
            _http.Dispose();
    }
}
