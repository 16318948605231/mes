using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mes.Core.Enums;
using Mes.Core.Exceptions;

namespace Mes.Core.Serialization;

/// <summary>
/// 基于 System.Text.Json 的默认序列化器。
/// </summary>
public sealed class JsonMesSerializer : IMesSerializer
{
    /// <summary>默认的 JSON 选项（驼峰、忽略 null、枚举转字符串、宽松读取）。</summary>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    private readonly JsonSerializerOptions _options;

    /// <summary>使用默认选项构造。</summary>
    public JsonMesSerializer() : this(DefaultOptions) { }

    /// <summary>使用自定义选项构造。</summary>
    public JsonMesSerializer(JsonSerializerOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <inheritdoc />
    public MesPayloadFormat Format => MesPayloadFormat.Json;

    /// <summary>创建默认 JSON 选项。</summary>
    public static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <inheritdoc />
    public byte[] Serialize(object? value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, _options);
        }
        catch (Exception ex)
        {
            throw new MesSerializationException($"JSON 序列化失败: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public string SerializeToString(object? value)
    {
        try
        {
            return JsonSerializer.Serialize(value, _options);
        }
        catch (Exception ex)
        {
            throw new MesSerializationException($"JSON 序列化失败: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public T? Deserialize<T>(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(data, _options);
        }
        catch (Exception ex)
        {
            throw new MesSerializationException($"JSON 反序列化失败: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public object? Deserialize(byte[]? data, Type type)
    {
        if (data is null || data.Length == 0)
            return null;
        try
        {
            return JsonSerializer.Deserialize(data, type, _options);
        }
        catch (Exception ex)
        {
            throw new MesSerializationException($"JSON 反序列化失败: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public T? DeserializeFromString<T>(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(text, _options);
        }
        catch (Exception ex)
        {
            throw new MesSerializationException($"JSON 反序列化失败: {ex.Message}", ex);
        }
    }
}
