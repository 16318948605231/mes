using Mes.Core.Enums;

namespace Mes.Core.Serialization;

/// <summary>
/// 序列化器抽象。默认实现基于 System.Text.Json。
/// </summary>
public interface IMesSerializer
{
    /// <summary>该序列化器的主内容类型（如 application/json）。</summary>
    string ContentType { get; }

    /// <summary>支持的负载格式。</summary>
    MesPayloadFormat Format { get; }

    /// <summary>序列化为字节。</summary>
    byte[] Serialize(object? value);

    /// <summary>序列化为字符串。</summary>
    string SerializeToString(object? value);

    /// <summary>从字节反序列化。</summary>
    T? Deserialize<T>(byte[]? data);

    /// <summary>从字节反序列化为指定类型。</summary>
    object? Deserialize(byte[]? data, Type type);

    /// <summary>从字符串反序列化。</summary>
    T? DeserializeFromString<T>(string? text);
}
