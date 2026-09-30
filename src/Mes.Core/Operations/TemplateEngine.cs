using System.Text;

namespace Mes.Core.Operations;

/// <summary>
/// 极简模板引擎：将 <c>{name}</c> 占位符替换为参数字典中的值。
/// 用于把操作键映射到具体通道/端点（如 REST URL、MQTT 主题）。
/// </summary>
public static class TemplateEngine
{
    /// <summary>
    /// 渲染模板。未提供的占位符将保留原样或按 <paramref name="removeMissing"/> 移除。
    /// 使用 <c>{name}</c> 语法；<c>{name:url}</c> 会对值做 URL 编码。
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, object?> args, bool removeMissing = false)
    {
        if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0)
            return template;

        var sb = new StringBuilder(template.Length + 16);
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '{')
            {
                int end = template.IndexOf('}', i + 1);
                if (end < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                var token = template.Substring(i + 1, end - i - 1);
                string name = token;
                string? modifier = null;
                int colon = token.IndexOf(':');
                if (colon >= 0)
                {
                    name = token[..colon];
                    modifier = token[(colon + 1)..];
                }

                if (args.TryGetValue(name, out var value) && value is not null)
                {
                    var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    if (string.Equals(modifier, "url", StringComparison.OrdinalIgnoreCase))
                        text = Uri.EscapeDataString(text);
                    sb.Append(text);
                }
                else if (!removeMissing)
                {
                    sb.Append('{').Append(token).Append('}');
                }

                i = end + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>提取模板中的占位符名称集合。</summary>
    public static IReadOnlyList<string> ExtractTokens(string template)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(template))
            return result;

        int i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                int end = template.IndexOf('}', i + 1);
                if (end < 0) break;
                var token = template.Substring(i + 1, end - i - 1);
                int colon = token.IndexOf(':');
                result.Add(colon >= 0 ? token[..colon] : token);
                i = end + 1;
            }
            else
            {
                i++;
            }
        }

        return result;
    }
}
