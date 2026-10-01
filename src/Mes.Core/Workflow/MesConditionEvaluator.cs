using System.Globalization;

namespace Mes.Core.Workflow;

/// <summary>
/// 内置极简条件求值器，用于步骤的 <c>When</c> 字段。
/// 仅支持必要的轻量比较，不引入任何表达式/脚本库：
/// <list type="bullet">
/// <item>比较：<c>==  !=  &gt;  &gt;=  &lt;  &lt;=</c>（两侧可为上下文变量或字面量）。</item>
/// <item>真值：裸变量（如 <c>enabled</c>）或取反（<c>!enabled</c>）。</item>
/// </list>
/// 左右操作数优先按上下文点路径解析，解析失败则按字面量处理；
/// 两侧都能解析为数值时按数值比较，否则按不区分大小写的字符串比较。
/// </summary>
public static class MesConditionEvaluator
{
    private static readonly string[] Operators = { "==", "!=", ">=", "<=", ">", "<" };

    /// <summary>求值。表达式为空视为 <c>true</c>。</summary>
    public static bool Evaluate(string? expression, MesWorkflowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(expression))
            return true;

        var expr = expression.Trim();

        foreach (var op in Operators)
        {
            var idx = IndexOfOperator(expr, op);
            if (idx < 0)
                continue;

            var leftToken = expr[..idx].Trim();
            var rightToken = expr[(idx + op.Length)..].Trim();
            var left = ResolveOperand(leftToken, context);
            var right = ResolveOperand(rightToken, context);
            return Compare(left, right, op);
        }

        // 无运算符：真值判断（支持前缀 '!' 取反）。
        var negate = false;
        var token = expr;
        while (token.StartsWith('!'))
        {
            negate = !negate;
            token = token[1..].Trim();
        }

        var value = ResolveOperand(token, context);
        var truth = IsTruthy(value);
        return negate ? !truth : truth;
    }

    private static int IndexOfOperator(string expr, string op)
    {
        // 避免把 ">=" 误判为 ">"：两字符运算符整体查找；单字符运算符需排除紧随的 '='。
        if (op.Length == 1)
        {
            for (int i = 0; i < expr.Length; i++)
            {
                if (expr[i] != op[0])
                    continue;
                // 跳过 '==' '!=' '>=' '<=' 的一部分
                if (i + 1 < expr.Length && expr[i + 1] == '=')
                    continue;
                if (i > 0 && (expr[i - 1] == '!' || expr[i - 1] == '>' || expr[i - 1] == '<' || expr[i - 1] == '='))
                    continue;
                return i;
            }
            return -1;
        }

        return expr.IndexOf(op, StringComparison.Ordinal);
    }

    private static object? ResolveOperand(string token, MesWorkflowContext context)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        // 带引号的字面量。
        if (token.Length >= 2 &&
            ((token[0] == '\'' && token[^1] == '\'') || (token[0] == '"' && token[^1] == '"')))
        {
            return token[1..^1];
        }

        // 优先按上下文点路径解析。
        if (context.TryResolvePath(token, out var value))
            return value;

        // 否则按字面量（数值/布尔/null/字符串）处理。
        if (string.Equals(token, "null", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
            return false;

        return token;
    }

    private static bool Compare(object? left, object? right, string op)
    {
        // 数值比较优先。
        if (TryToDouble(left, out var ld) && TryToDouble(right, out var rd))
        {
            return op switch
            {
                "==" => ld == rd,
                "!=" => ld != rd,
                ">" => ld > rd,
                ">=" => ld >= rd,
                "<" => ld < rd,
                "<=" => ld <= rd,
                _ => false,
            };
        }

        // 布尔比较（仅等值）。
        if (left is bool lb && right is bool rb)
        {
            return op switch
            {
                "==" => lb == rb,
                "!=" => lb != rb,
                _ => false,
            };
        }

        // 字符串比较（不区分大小写）。
        var ls = MesWorkflowContext.ToInvariantString(left) ?? string.Empty;
        var rs = MesWorkflowContext.ToInvariantString(right) ?? string.Empty;
        var cmp = string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "==" => cmp == 0,
            "!=" => cmp != 0,
            ">" => cmp > 0,
            ">=" => cmp >= 0,
            "<" => cmp < 0,
            "<=" => cmp <= 0,
            _ => false,
        };
    }

    private static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case long l:
                result = l;
                return true;
            case int i:
                result = i;
                return true;
            case bool:
                result = 0;
                return false;
            default:
                return double.TryParse(
                    MesWorkflowContext.ToInvariantString(value),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }
    }

    private static bool IsTruthy(object? value)
        => value switch
        {
            null => false,
            bool b => b,
            double d => d != 0,
            float f => f != 0,
            long l => l != 0,
            int i => i != 0,
            string s => s.Length > 0
                        && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)
                        && s != "0",
            _ => true,
        };
}
