using Mes.Core.Enums;

namespace Mes.Core.Models;

/// <summary>
/// 过站校验结果（<c>CheckUnitPassed</c> 操作的响应模型）。
/// </summary>
public sealed class GateCheckResult
{
    /// <summary>是否放行。</summary>
    public bool Passed { get; set; }

    /// <summary>原因/说明。</summary>
    public string? Reason { get; set; }

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}

/// <summary>
/// 图像上传的默认传输 DTO（Base64 内联 + 元数据）。真实服务器若使用 multipart，
/// 可通过覆盖操作绑定或自定义传输实现替换。
/// </summary>
public sealed class ImageUploadDto
{
    /// <summary>图像标识。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>文件名。</summary>
    public string? FileName { get; set; }

    /// <summary>内容类型。</summary>
    public string? ContentType { get; set; }

    /// <summary>格式。</summary>
    public ImageFormat Format { get; set; }

    /// <summary>宽度。</summary>
    public int? Width { get; set; }

    /// <summary>高度。</summary>
    public int? Height { get; set; }

    /// <summary>SHA-256。</summary>
    public string? Sha256 { get; set; }

    /// <summary>Base64 编码的图像数据。</summary>
    public string? DataBase64 { get; set; }
}
