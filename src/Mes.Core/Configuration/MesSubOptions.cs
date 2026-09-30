using Mes.Core.Enums;

namespace Mes.Core.Configuration;

/// <summary>
/// 连接端点配置。不同协议按需使用其中字段。
/// </summary>
public sealed class MesEndpointOptions
{
    /// <summary>基础地址（REST 基础 URL、OPC UA endpoint、数据库连接字符串等）。</summary>
    public string? BaseAddress { get; set; }

    /// <summary>主机名（MQTT/数据库/SECS 等）。</summary>
    public string? Host { get; set; }

    /// <summary>端口。</summary>
    public int? Port { get; set; }

    /// <summary>是否启用 TLS/SSL。</summary>
    public bool UseTls { get; set; }
}

/// <summary>
/// 认证配置。
/// </summary>
public sealed class MesAuthOptions
{
    /// <summary>认证类型。</summary>
    public MesAuthType Type { get; set; } = MesAuthType.None;

    /// <summary>用户名。</summary>
    public string? Username { get; set; }

    /// <summary>密码。</summary>
    public string? Password { get; set; }

    /// <summary>令牌（Bearer）。</summary>
    public string? Token { get; set; }

    /// <summary>API Key。</summary>
    public string? ApiKey { get; set; }

    /// <summary>API Key 所在的请求头名称（默认 X-Api-Key）。</summary>
    public string ApiKeyHeader { get; set; } = "X-Api-Key";

    /// <summary>OAuth2 令牌端点。</summary>
    public string? TokenUrl { get; set; }

    /// <summary>OAuth2 客户端 Id。</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth2 客户端密钥。</summary>
    public string? ClientSecret { get; set; }

    /// <summary>OAuth2 作用域。</summary>
    public string? Scope { get; set; }

    /// <summary>客户端证书路径。</summary>
    public string? CertificatePath { get; set; }

    /// <summary>客户端证书密码。</summary>
    public string? CertificatePassword { get; set; }
}

/// <summary>
/// 重试/退避配置。
/// </summary>
public sealed class MesRetryOptions
{
    /// <summary>是否启用重试。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>最大重试次数（不含首次）。</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>基础延迟（毫秒）。</summary>
    public int BaseDelayMs { get; set; } = 500;

    /// <summary>最大延迟（毫秒）。</summary>
    public int MaxDelayMs { get; set; } = 10_000;

    /// <summary>指数退避因子。</summary>
    public double BackoffFactor { get; set; } = 2.0;

    /// <summary>是否对超时也重试。</summary>
    public bool RetryOnTimeout { get; set; } = true;
}

/// <summary>
/// TLS 配置。
/// </summary>
public sealed class MesTlsOptions
{
    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; }

    /// <summary>是否允许不受信任的证书（自签名，仅测试环境建议开启）。</summary>
    public bool AllowUntrustedCertificates { get; set; }

    /// <summary>CA 证书路径。</summary>
    public string? CaCertificatePath { get; set; }

    /// <summary>客户端证书路径。</summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>客户端证书密码。</summary>
    public string? ClientCertificatePassword { get; set; }
}

/// <summary>
/// 图像处理配置。
/// </summary>
public sealed class MesImageOptions
{
    /// <summary>默认图像传输模式（可被单张图像的设置覆盖）。</summary>
    public ImageTransferMode DefaultTransferMode { get; set; } = ImageTransferMode.SeparateUpload;

    /// <summary>用于单独上传的操作键。</summary>
    public string UploadOperationKey { get; set; } = Operations.MesOperationKeys.UploadImage;

    /// <summary>内嵌图像的最大字节数（超过则自动改为单独上传）。0 表示不限制。</summary>
    public long MaxEmbeddedBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>是否在传输前计算 SHA-256。</summary>
    public bool ComputeSha256 { get; set; } = true;
}
