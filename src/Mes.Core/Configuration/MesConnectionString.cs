using System.Text;
using Mes.Core.Enums;
using Mes.Core.Exceptions;

namespace Mes.Core.Configuration;

/// <summary>
/// 连接字符串 / URI 解析器：把一行 <c>scheme://host:port/path?opt=val</c>
/// 解析为 <see cref="MesProtocolKind"/> 并填充 <see cref="MesOptions"/>（端点、认证、协议专有属性）。
/// <para>
/// 目的是让接入方“换协议只改一个字符串”，甚至把字符串放进配置文件。所支持的 scheme：
/// <list type="bullet">
/// <item><c>mem</c> / <c>inmemory</c> → 进程内回环</item>
/// <item><c>rest</c> / <c>http</c> / <c>https</c> → REST</item>
/// <item><c>mqtt</c> / <c>mqtts</c> → MQTT</item>
/// <item><c>opc.tcp</c> / <c>opcua</c> → OPC UA</item>
/// <item><c>file</c> → 文件落地</item>
/// <item><c>db</c> / <c>sqlite</c> / <c>mssql</c>(<c>sqlserver</c>) → 数据库</item>
/// <item><c>secs</c> / <c>hsms</c> → SECS/GEM</item>
/// <item><c>soap</c> / <c>soaps</c> → SOAP/WCF</item>
/// <item><c>kafka</c> → Apache Kafka</item>
/// <item><c>amqp</c> / <c>amqps</c> / <c>rabbitmq</c> → AMQP（RabbitMQ）</item>
/// <item><c>ws</c> / <c>wss</c> → WebSocket</item>
/// <item><c>grpc</c> / <c>grpcs</c> → gRPC</item>
/// </list>
/// 任何未识别的查询参数都会原样写入 <see cref="MesOptions.Properties"/>，供各 Provider 读取。
/// </para>
/// </summary>
public static class MesConnectionString
{
    /// <summary>把连接字符串解析为一个新的 <see cref="MesOptions"/>。</summary>
    /// <param name="connectionString">形如 <c>mqtt://host:1883?prefix=mes</c> 的连接字符串。</param>
    /// <param name="configure">可选的后置配置回调（在解析之后执行，便于覆盖个别字段）。</param>
    public static MesOptions Parse(string connectionString, Action<MesOptions>? configure = null)
    {
        var options = new MesOptions();
        Apply(options, connectionString);
        configure?.Invoke(options);
        return options;
    }

    /// <summary>仅解析出协议种类（不修改任何配置）。</summary>
    public static MesProtocolKind ResolveProtocol(string connectionString)
    {
        var scheme = ExtractScheme(connectionString);
        return MapScheme(scheme);
    }

    /// <summary>解析连接字符串并把结果写入既有的 <paramref name="options"/>，返回解析出的协议。</summary>
    public static MesProtocolKind Apply(MesOptions options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new MesConfigurationException("连接字符串不能为空。");

        var scheme = ExtractScheme(connectionString);
        var protocol = MapScheme(scheme);
        options.Protocol = protocol;

        var remainder = connectionString.Substring(scheme.Length + 1); // 去掉 "scheme:"
        var isUri = remainder.StartsWith("//", StringComparison.Ordinal);

        if (isUri)
        {
            var uri = ParseUri(connectionString, scheme);
            ApplyUri(options, protocol, scheme, uri);
        }
        else
        {
            // 原始模式：scheme:<payload>，主要供数据库/文件落地传入原生连接串或路径。
            ApplyRaw(options, protocol, scheme, remainder);
        }

        return protocol;
    }

    // ---------------- 内部实现 ----------------

    private static string ExtractScheme(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new MesConfigurationException("连接字符串不能为空。");
        var idx = connectionString.IndexOf(':');
        if (idx <= 0)
            throw new MesConfigurationException($"连接字符串缺少 scheme（形如 'mqtt://...'）：{connectionString}");
        return connectionString.Substring(0, idx).Trim().ToLowerInvariant();
    }

    private static MesProtocolKind MapScheme(string scheme) => scheme switch
    {
        "mem" or "inmemory" or "loopback" => MesProtocolKind.InMemory,
        "rest" or "http" or "https" => MesProtocolKind.Rest,
        "mqtt" or "mqtts" or "tcp" => MesProtocolKind.Mqtt,
        "opc.tcp" or "opcua" or "opc" => MesProtocolKind.OpcUa,
        "file" or "filedrop" => MesProtocolKind.FileDrop,
        "db" or "database" or "sqlite" or "mssql" or "sqlserver" or "sql" => MesProtocolKind.Database,
        "secs" or "secsgem" or "hsms" or "gem" => MesProtocolKind.SecsGem,
        "soap" or "soaps" or "wcf" => MesProtocolKind.Soap,
        "kafka" => MesProtocolKind.Kafka,
        "amqp" or "amqps" or "rabbitmq" => MesProtocolKind.Amqp,
        "ws" or "wss" or "websocket" or "signalr" => MesProtocolKind.WebSocket,
        "grpc" or "grpcs" => MesProtocolKind.Grpc,
        _ => throw new MesConfigurationException($"不支持的连接字符串 scheme：'{scheme}'。")
    };

    private static Uri ParseUri(string connectionString, string scheme)
    {
        // System.Uri 无法直接处理带点号的 scheme（如 opc.tcp），统一归一化后再解析。
        var normalizedScheme = scheme.Contains('.') ? scheme.Replace('.', '-') : scheme;
        var normalized = normalizedScheme + connectionString.Substring(scheme.Length);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            throw new MesConfigurationException($"无法解析连接字符串：{connectionString}");
        return uri;
    }

    private static void ApplyUri(MesOptions options, MesProtocolKind protocol, string scheme, Uri uri)
    {
        var query = ParseQuery(uri.Query);
        var secure = scheme is "https" or "mqtts" or "soaps" or "amqps" or "wss" or "grpcs"
                     || GetBool(query, "tls", "usetls", "secure");
        var host = string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
        var port = uri.Port > 0 ? uri.Port : (int?)null;
        var path = uri.AbsolutePath;

        ApplyAuth(options, uri.UserInfo, query);

        switch (protocol)
        {
            case MesProtocolKind.InMemory:
                break;

            case MesProtocolKind.Rest:
                options.Endpoint.BaseAddress = BuildHttpUrl(secure ? "https" : "http", host, port, path);
                options.Endpoint.UseTls = secure;
                break;

            case MesProtocolKind.Soap:
                options.Endpoint.BaseAddress = BuildHttpUrl(secure ? "https" : "http", host, port, path);
                options.Endpoint.UseTls = secure;
                break;

            case MesProtocolKind.WebSocket:
                options.Endpoint.BaseAddress = BuildHttpUrl(secure ? "wss" : "ws", host, port, path);
                options.Endpoint.UseTls = secure;
                break;

            case MesProtocolKind.Grpc:
                options.Endpoint.BaseAddress = BuildHttpUrl(secure ? "https" : "http", host, port ?? (secure ? 443 : 80), path);
                options.Endpoint.Host = host;
                options.Endpoint.Port = port;
                options.Endpoint.UseTls = secure;
                break;

            case MesProtocolKind.OpcUa:
                options.Endpoint.BaseAddress = BuildHttpUrl("opc.tcp", host, port, path);
                options.Endpoint.Host = host;
                options.Endpoint.Port = port;
                break;

            case MesProtocolKind.Mqtt:
                options.Endpoint.Host = host;
                options.Endpoint.Port = port;
                options.Endpoint.UseTls = secure;
                break;

            case MesProtocolKind.Amqp:
                options.Endpoint.Host = host;
                options.Endpoint.Port = port;
                options.Endpoint.UseTls = secure;
                var vhost = path.TrimStart('/');
                if (!string.IsNullOrEmpty(vhost))
                    options.SetProperty("VirtualHost", Uri.UnescapeDataString(vhost));
                break;

            case MesProtocolKind.Kafka:
                options.Endpoint.Host = host;
                options.Endpoint.Port = port;
                options.Endpoint.UseTls = secure;
                var bootstrap = GetValue(query, "bootstrap", "bootstrapservers")
                                ?? (host is null ? null : port is null ? host : $"{host}:{port}");
                if (!string.IsNullOrEmpty(bootstrap))
                    options.SetProperty("BootstrapServers", bootstrap);
                break;

            case MesProtocolKind.SecsGem:
                options.Endpoint.Host = host;
                options.Endpoint.Port = port;
                break;

            case MesProtocolKind.FileDrop:
                options.Endpoint.BaseAddress = BuildFilePath(uri);
                break;

            case MesProtocolKind.Database:
                ApplyDatabaseUri(options, scheme, uri, query);
                break;
        }

        ApplyQueryProperties(options, protocol, query);
    }

    private static void ApplyRaw(MesOptions options, MesProtocolKind protocol, string scheme, string payload)
    {
        payload = payload.Trim();
        switch (protocol)
        {
            case MesProtocolKind.InMemory:
                break;

            case MesProtocolKind.FileDrop:
                options.Endpoint.BaseAddress = payload;
                break;

            case MesProtocolKind.Database:
                if (scheme is "sqlite")
                {
                    options.SetProperty("Provider", "Sqlite");
                    options.Endpoint.BaseAddress = payload.Contains('=', StringComparison.Ordinal)
                        ? payload
                        : $"Data Source={payload}";
                }
                else
                {
                    // db:<原生 ADO 连接串>
                    options.Endpoint.BaseAddress = payload;
                    if (options.GetProperty("Provider") is null)
                        options.SetProperty("Provider", "Sqlite");
                }
                break;

            default:
                throw new MesConfigurationException(
                    $"scheme '{scheme}' 需要使用 URI 形式（'{scheme}://host...'），而不是 '{scheme}:{payload}'。");
        }
    }

    private static void ApplyDatabaseUri(MesOptions options, string scheme, Uri uri, IDictionary<string, string> query)
    {
        var provider = GetValue(query, "provider")
                       ?? (scheme is "mssql" or "sqlserver" or "sql" ? "SqlServer" : "Sqlite");
        options.SetProperty("Provider", provider);

        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var dataSource = BuildFilePath(uri);
            options.Endpoint.BaseAddress = $"Data Source={dataSource}";
            return;
        }

        // SqlServer：由 URI 组装最小连接串，其余查询参数原样附加。
        var sb = new StringBuilder();
        var server = uri.Host;
        if (uri.Port > 0) server += $",{uri.Port}";
        sb.Append("Server=").Append(server).Append(';');
        var database = uri.AbsolutePath.TrimStart('/');
        if (!string.IsNullOrEmpty(database))
            sb.Append("Database=").Append(Uri.UnescapeDataString(database)).Append(';');

        var userInfo = uri.UserInfo;
        if (!string.IsNullOrEmpty(userInfo))
        {
            var parts = userInfo.Split(':', 2);
            sb.Append("User Id=").Append(Uri.UnescapeDataString(parts[0])).Append(';');
            if (parts.Length > 1)
                sb.Append("Password").Append('=').Append(Uri.UnescapeDataString(parts[1])).Append(';');
        }

        foreach (var kv in query)
        {
            if (string.Equals(kv.Key, "provider", StringComparison.OrdinalIgnoreCase)) continue;
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        }

        options.Endpoint.BaseAddress = sb.ToString();
    }

    private static void ApplyAuth(MesOptions options, string userInfo, IDictionary<string, string> query)
    {
        var token = GetValue(query, "token", "bearer");
        var apiKey = GetValue(query, "apikey", "api_key");
        var user = GetValue(query, "username", "user");
        var pass = GetValue(query, "password", "pass");

        if (!string.IsNullOrEmpty(userInfo))
        {
            var parts = userInfo.Split(':', 2);
            user ??= Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1) pass ??= Uri.UnescapeDataString(parts[1]);
        }

        if (!string.IsNullOrEmpty(token))
        {
            options.Auth.Type = MesAuthType.BearerToken;
            options.Auth.Token = token;
        }
        else if (!string.IsNullOrEmpty(apiKey))
        {
            options.Auth.Type = MesAuthType.ApiKey;
            options.Auth.ApiKey = apiKey;
            var header = GetValue(query, "apikeyheader");
            if (!string.IsNullOrEmpty(header)) options.Auth.ApiKeyHeader = header;
        }
        else if (!string.IsNullOrEmpty(user))
        {
            options.Auth.Type = MesAuthType.UsernamePassword;
            options.Auth.Username = user;
            options.Auth.Password = pass;
        }
    }

    private static void ApplyQueryProperties(MesOptions options, MesProtocolKind protocol, IDictionary<string, string> query)
    {
        // 已被端点/认证消费的查询键；其余原样进入 Properties 供各 Provider 读取。
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tls", "usetls", "secure", "token", "bearer", "apikey", "api_key",
            "apikeyheader", "username", "user", "password", "pass",
            "bootstrap", "bootstrapservers", "provider"
        };

        foreach (var kv in query)
        {
            if (consumed.Contains(kv.Key)) continue;
            var key = NormalizePropertyKey(kv.Key);
            options.Properties[key] = kv.Value;
        }
    }

    private static string NormalizePropertyKey(string key) => key.ToLowerInvariant() switch
    {
        "prefix" or "topicprefix" => "TopicPrefix",
        "clientid" => "ClientId",
        "group" or "groupid" => "GroupId",
        "topic" => "Topic",
        "queue" => "Queue",
        "exchange" => "Exchange",
        "routingkey" => "RoutingKey",
        "virtualhost" or "vhost" => "VirtualHost",
        "deviceid" => "DeviceId",
        "securitypolicy" => "SecurityPolicy",
        "ensureschema" => "EnsureSchema",
        "processedfolder" => "ProcessedFolder",
        "soapaction" => "SoapAction",
        "namespace" or "ns" => "Namespace",
        _ => key
    };

    private static string? GetValue(IDictionary<string, string> query, params string[] keys)
    {
        foreach (var k in keys)
            if (query.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v))
                return v;
        return null;
    }

    private static bool GetBool(IDictionary<string, string> query, params string[] keys)
    {
        var v = GetValue(query, keys);
        return v is not null && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                                          || v.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static IDictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return dict;
        var q = query.StartsWith('?') ? query.Substring(1) : query;
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                dict[Uri.UnescapeDataString(pair)] = "true";
            else
                dict[Uri.UnescapeDataString(pair.Substring(0, eq))] = Uri.UnescapeDataString(pair.Substring(eq + 1));
        }
        return dict;
    }

    private static string BuildHttpUrl(string scheme, string? host, int? port, string path)
    {
        var sb = new StringBuilder();
        sb.Append(scheme).Append("://").Append(host ?? "localhost");
        if (port is > 0) sb.Append(':').Append(port.Value);
        if (!string.IsNullOrEmpty(path) && path != "/")
            sb.Append(path.TrimEnd('/'));
        return sb.ToString();
    }

    private static string BuildFilePath(Uri uri)
    {
        // file:///var/mes → /var/mes；file:///C:/mes → C:/mes；file://server/share → \\server\share
        if (!string.IsNullOrEmpty(uri.Host) && uri.Host != "localhost")
            return $@"\\{uri.Host}{uri.AbsolutePath.Replace('/', '\\')}";

        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':')
            path = path.Substring(1); // /C:/mes → C:/mes
        return path;
    }
}
