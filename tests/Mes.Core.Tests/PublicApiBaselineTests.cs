using System.Reflection;
using System.Text;
using Mes.Core.Client;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 公有 API 基线快照测试：对 <c>Mes.Core</c> 的公开表面（类型 + 成员）生成稳定快照，
/// 与仓库中登记的基线文件比对，避免升级无意破坏已接入方的公有 API。
/// <para>
/// 如为“有意的 API 变更”，设置环境变量 <c>MES_UPDATE_API_BASELINE=1</c> 重新运行以更新基线，
/// 并将更新后的 <c>ApiBaseline/Mes.Core.PublicApi.txt</c> 一并提交。
/// </para>
/// </summary>
public sealed class PublicApiBaselineTests
{
    [Fact]
    public void CorePublicApi_MatchesBaseline()
    {
        var assembly = typeof(IMesClient).Assembly;
        var snapshot = BuildSnapshot(assembly);

        var baselinePath = LocateBaselineFile();
        var update = Environment.GetEnvironmentVariable("MES_UPDATE_API_BASELINE") == "1";

        if (update || !File.Exists(baselinePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, snapshot);
            return;
        }

        var baseline = File.ReadAllText(baselinePath).Replace("\r\n", "\n").Trim();
        var current = snapshot.Replace("\r\n", "\n").Trim();

        if (!string.Equals(baseline, current, StringComparison.Ordinal))
        {
            var diff = FirstDifference(baseline, current);
            Assert.Fail(
                "Mes.Core 公有 API 与基线不一致（可能破坏已接入方）。" +
                "若为有意变更，请设置 MES_UPDATE_API_BASELINE=1 重跑以更新基线并提交。\n" + diff);
        }
    }

    private static string BuildSnapshot(Assembly assembly)
    {
        var lines = new List<string>();
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            lines.Add($"TYPE {Describe(type)}");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var m in type.GetMembers(flags)
                         .Where(m => m is not MethodInfo mi || !mi.IsSpecialName)
                         .Select(FormatMember)
                         .Where(s => s is not null)
                         .OrderBy(s => s, StringComparer.Ordinal))
            {
                lines.Add("  " + m);
            }
        }
        return string.Join("\n", lines);
    }

    private static string Describe(Type t)
    {
        var kind = t.IsInterface ? "interface" : t.IsEnum ? "enum" : t.IsValueType ? "struct" : "class";
        return $"{kind} {t.FullName}";
    }

    private static string? FormatMember(MemberInfo member) => member switch
    {
        MethodInfo mi => $"method {mi.ReturnType.Name} {mi.Name}({string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name))})",
        PropertyInfo pi => $"property {pi.PropertyType.Name} {pi.Name}",
        FieldInfo fi => $"field {fi.FieldType.Name} {fi.Name}",
        EventInfo ei => $"event {ei.EventHandlerType?.Name} {ei.Name}",
        ConstructorInfo ci => $"ctor ({string.Join(", ", ci.GetParameters().Select(p => p.ParameterType.Name))})",
        _ => null
    };

    private static string FirstDifference(string baseline, string current)
    {
        var a = baseline.Split('\n');
        var b = current.Split('\n');
        var max = Math.Max(a.Length, b.Length);
        for (var i = 0; i < max; i++)
        {
            var la = i < a.Length ? a[i] : "<无>";
            var lb = i < b.Length ? b[i] : "<无>";
            if (!string.Equals(la, lb, StringComparison.Ordinal))
                return $"首个差异在第 {i + 1} 行：\n  基线: {la}\n  当前: {lb}";
        }
        return "（无法定位具体差异行）";
    }

    private static string LocateBaselineFile()
    {
        // 从测试程序集所在目录向上回溯到项目根（含 .csproj），定位 ApiBaseline 目录。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mes.Core.Tests.csproj")))
            dir = dir.Parent;
        var projectDir = dir?.FullName ?? AppContext.BaseDirectory;
        return Path.Combine(projectDir, "ApiBaseline", "Mes.Core.PublicApi.txt");
    }
}
