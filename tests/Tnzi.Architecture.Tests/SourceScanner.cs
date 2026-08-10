using System.Text.RegularExpressions;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 直接从源码树读取「这个模块有哪些实体 / 控制器」的事实。
/// </summary>
/// <remarks>
/// <para>
/// 用于对账 <c>docs/modules-index.json</c>。那个文件被 <c>Tnzi.Mcp</c> 当作模块元数据的
/// <b>权威来源</b>（<c>DocIndex</c> 优先读它、读不到才回退文件系统扫描），而它的代码注释
/// 声称「由构建脚本预生成」—— 实际上仓库里<b>没有任何生成脚本</b>，它一直是手工维护的。
/// 结果就是持续漂移：对账首次运行时 entities 缺 47 个（<c>AI</c> 模块索引里是空数组，
/// 代码里有 22 个实体）、controllers 缺 42 个，另有 3 个实体和 1 个控制器是早已删除或
/// 随模块拆分搬走却没清理的。
/// </para>
/// <para>
/// 正则而非 Roslyn：门禁只需要类型<b>名字</b>，语法树是杀鸡用牛刀，且会给测试项目引入
/// 一份编译器依赖。代价是必须严格贴着框架的目录与继承约定，见下面各方法的说明。
/// </para>
/// </remarks>
internal static class SourceScanner
{
    private static readonly Regex ClassWithBases = new(
        @"public\s+((?:sealed\s+|abstract\s+|partial\s+)*)class\s+(\w+)\s*:\s*([^{]+)\{",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// 实体基类/接口的识别。
    /// </summary>
    /// <remarks>
    /// ★ 写成 <c>\w*Entity\w*\s*&lt;</c> 而不是 <c>\bEntity&lt;</c>：驼峰命名里
    /// <c>MultiTenantAuditedEntity&lt;</c> 的 <c>dEntity</c> 之间<b>没有词边界</b>，
    /// 用 <c>\b</c> 会一个都匹配不上；同理 <c>EntityBase&lt;</c> 也不含 <c>Entity&lt;</c>。
    /// 这两个坑我各踩了一次，扫出来的数字才从「全是 0」变成可信。
    /// <c>IdentityUser</c>/<c>IdentityRole</c> 是 ASP.NET Identity 基类，Identity 模块的
    /// 用户与角色实体继承它们（同时实现 <c>IEntity</c>，但基类列表里先出现的是它们）。
    /// </remarks>
    private static readonly Regex EntityBaseType = new(
        @"\w*Entity\w*\s*<|IEntity\b|IdentityUser\s*<|IdentityRole\s*<",
        RegexOptions.Compiled);

    private static readonly Regex ControllerClass = new(
        @"public\s+(?:abstract\s+|sealed\s+|partial\s+)*class\s+(\w*Controller)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// 某个程序集的实体类名。
    /// </summary>
    /// <remarks>
    /// <b>只扫 <c>Entities/</c> 顶层</b>：这是框架对实体的既定约定（<c>Entities/Configs/</c>
    /// 放的是 EF 配置类，不是实体）。已知边界 —— 不在该目录下的实体不计入，例如
    /// <c>Tnzi.EFCore/DocumentNumbering/DocumentSequence.cs</c>。索引此前把它错记在
    /// <c>Finance</c> 名下，按本规则它不属于任何模块的 entities，这是刻意的：
    /// 规则要能一句话说清，否则门禁自己就会变成下一个漂移源。
    /// </remarks>
    public static SortedSet<string> ScanEntities(string repoRoot, string assembly)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var dir = Path.Combine(repoRoot, "src", assembly, "Entities");
        if (!Directory.Exists(dir))
            return result;

        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            foreach (Match m in ClassWithBases.Matches(StripLineComments(File.ReadAllText(file))))
            {
                if (m.Groups[1].Value.Contains("abstract", StringComparison.Ordinal))
                    continue;
                if (EntityBaseType.IsMatch(m.Groups[3].Value))
                    result.Add(m.Groups[2].Value);
            }
        }

        return result;
    }

    /// <summary>某个程序集 <c>Controllers/</c> 下（含子目录）的全部公开控制器类名。</summary>
    /// <remarks>
    /// 收录<b>全部</b> <c>*Controller</c> 而不是只收 <c>[DefaultController]</c> 标记的：
    /// 两者在当前代码库结果完全相同（索引里 104 个控制器全是 <c>Default*</c>，源码里也没有
    /// 别的形态），取更简单的那条规则，且不必为此解析特性。
    /// </remarks>
    public static SortedSet<string> ScanControllers(string repoRoot, string assembly)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var dir = Path.Combine(repoRoot, "src", assembly, "Controllers");
        if (!Directory.Exists(dir))
            return result;

        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match m in ControllerClass.Matches(StripLineComments(File.ReadAllText(file))))
                result.Add(m.Groups[1].Value);
        }

        return result;
    }

    /// <summary>
    /// 剥掉行注释。
    /// </summary>
    /// <remarks>
    /// 不做这一步，注释里当反面教材写的示例代码会被当成真声明 ——
    /// <c>ImagingModule</c> 的注释里就原样引着一句 <c>[DependsOn(typeof(TnziCoreModule))]</c>。
    /// </remarks>
    private static string StripLineComments(string text)
        => string.Join('\n', text.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
