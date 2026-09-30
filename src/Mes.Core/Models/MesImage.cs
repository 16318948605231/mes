using Mes.Core.Enums;

namespace Mes.Core.Models;

/// <summary>
/// MES 图像载体。支持内嵌、单独上传、仅引用三种传输模式。
/// </summary>
public sealed class MesImage
{
    /// <summary>图像唯一标识（供缺陷/结果引用）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>文件名（含扩展名）。</summary>
    public string? FileName { get; set; }

    /// <summary>图像格式。</summary>
    public ImageFormat Format { get; set; } = ImageFormat.Unknown;

    /// <summary>MIME 内容类型（如 image/png）。</summary>
    public string? ContentType { get; set; }

    /// <summary>传输模式。</summary>
    public ImageTransferMode TransferMode { get; set; } = ImageTransferMode.SeparateUpload;

    /// <summary>图像字节（内嵌模式必填；单独上传模式可从磁盘读取）。</summary>
    public byte[]? Data { get; set; }

    /// <summary>本地路径（单独上传模式可提供，框架据此读取字节）。</summary>
    public string? LocalPath { get; set; }

    /// <summary>远端引用（仅引用模式必填；单独上传后由框架回填）。</summary>
    public string? RemoteUri { get; set; }

    /// <summary>存储键（对象存储 key，单独上传后回填）。</summary>
    public string? StorageKey { get; set; }

    /// <summary>宽度（像素）。</summary>
    public int? Width { get; set; }

    /// <summary>高度（像素）。</summary>
    public int? Height { get; set; }

    /// <summary>SHA-256 校验（十六进制，可选，用于去重/完整性校验）。</summary>
    public string? Sha256 { get; set; }

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();

    /// <summary>字节大小（若已知）。</summary>
    public long? Length => Data?.LongLength;

    /// <summary>依据文件名推断格式与内容类型。</summary>
    public static (ImageFormat Format, string ContentType) InferFormat(string? fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".png" => (ImageFormat.Png, "image/png"),
            ".jpg" or ".jpeg" => (ImageFormat.Jpeg, "image/jpeg"),
            ".bmp" => (ImageFormat.Bmp, "image/bmp"),
            ".tif" or ".tiff" => (ImageFormat.Tiff, "image/tiff"),
            ".webp" => (ImageFormat.Webp, "image/webp"),
            _ => (ImageFormat.Unknown, "application/octet-stream")
        };
    }

    /// <summary>从本地文件创建内嵌图像。</summary>
    public static MesImage FromFile(string path, ImageTransferMode mode = ImageTransferMode.SeparateUpload)
    {
        var (format, contentType) = InferFormat(path);
        return new MesImage
        {
            FileName = System.IO.Path.GetFileName(path),
            LocalPath = path,
            Format = format,
            ContentType = contentType,
            TransferMode = mode
        };
    }
}

/// <summary>
/// 图像上传回执。
/// </summary>
public sealed class ImageUploadReceipt
{
    /// <summary>对应的图像标识。</summary>
    public string ImageId { get; set; } = string.Empty;

    /// <summary>远端访问 URI。</summary>
    public string? RemoteUri { get; set; }

    /// <summary>存储键。</summary>
    public string? StorageKey { get; set; }

    /// <summary>字节大小。</summary>
    public long Size { get; set; }

    /// <summary>上传时间。</summary>
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>扩展属性。</summary>
    public IDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
}
