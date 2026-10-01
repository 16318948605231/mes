using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Mes.Core.Workflow;

/// <summary>
/// 工作流运行时上下文：一个字符串键的变量袋。
/// 应用在触发阶段前往里放入 <c>serialNumber/workOrderId/recipeId/outcome/inspectionResult/…</c>，
/// 步骤的 <c>Args/When/PayloadFrom/CaptureTo</c> 都基于它求值与写回。
/// 支持点路径取值（如 <c>recipe.Id</c>、<c>workOrder.Parameters.speed</c>），
/// 可穿透字典、<see cref="JsonElement"/> 以及普通对象属性。
/// </summary>
public sealed class MesWorkflowContext
{
    private readonly Dictionary<string, object?> _vars;

    /// <summary>创建空上下文（键不区分大小写）。</summary>
    public MesWorkflowContext()
        => _vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>基于初始变量创建上下文。</summary>
    public MesWorkflowContext(IEnumerable<KeyValuePair<string, object?>> initial)
        : this()
    {
        foreach (var kv in initial)
            _vars[kv.Key] = kv.Value;
    }

    /// <summary>当前所有变量（可读写）。</summary>
    public IDictionary<string, object?> Variables => _vars;

    /// <summary>设置一个变量并返回自身，便于链式写法。</summary>
    public MesWorkflowContext Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _vars[key] = value;
        return this;
    }

    /// <summary>读取一个顶层变量，不存在返回 <c>null</c>。</summary>
    public object? Get(string key)
        => _vars.TryGetValue(key, out var v) ? v : null;

    /// <summary>尝试读取一个顶层变量。</summary>
    public bool TryGet(string key, out object? value)
        => _vars.TryGetValue(key, out value);

    /// <summary>移除一个顶层变量。</summary>
    public bool Remove(string key) => _vars.Remove(key);

    /// <summary>
    /// 按点路径解析取值（如 <c>recipe.Id</c>）。叶子值会被规范化为标量
    /// （字符串/数值/布尔），便于模板与条件求值使用。
    /// </summary>
    public bool TryResolvePath(string path, out object? value)
    {
        value = null;
        if (string.IsNullOrEmpty(path))
            return false;

        var segments = path.Split('.');
        if (!_vars.TryGetValue(segments[0], out var current))
            return false;

        for (int i = 1; i < segments.Length; i++)
        {
            if (current is null || !TryGetMember(current, segments[i], out current))
                return false;
        }

        value = Normalize(current);
        return true;
    }

    private static bool TryGetMember(object target, string name, out object? value)
    {
        value = null;

        switch (target)
        {
            case JsonElement json:
                if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var prop))
                {
                    value = prop;
                    return true;
                }
                return false;

            case IDictionary<string, object?> typed:
                return typed.TryGetValue(name, out value);

            case IDictionary raw:
                foreach (DictionaryEntry e in raw)
                {
                    if (e.Key is string k && string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = e.Value;
                        return true;
                    }
                }
                return false;

            default:
                var pi = target.GetType().GetProperty(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.IgnoreCase);
                if (pi is not null && pi.CanRead)
                {
                    value = pi.GetValue(target);
                    return true;
                }
                return false;
        }
    }

    /// <summary>把 <see cref="JsonElement"/> 叶子转换为 .NET 标量，其余原样返回。</summary>
    internal static object? Normalize(object? value)
    {
        if (value is not JsonElement json)
            return value;

        return json.ValueKind switch
        {
            JsonValueKind.String => json.GetString(),
            JsonValueKind.Number => json.TryGetInt64(out var l)
                ? l
                : json.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            // 对象/数组：保留原 JsonElement，供进一步点路径穿透或序列化。
            _ => json,
        };
    }

    /// <summary>把标量值转换为不变区域性的字符串（用于参数/诊断）。</summary>
    internal static string? ToInvariantString(object? value)
        => value switch
        {
            null => null,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
}
