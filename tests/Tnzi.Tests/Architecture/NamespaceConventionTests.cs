using System.Text.RegularExpressions;

namespace Tnzi.Tests.Architecture;

/// <summary>
/// 命名空间与目录的约定门禁（规范见 docs/coding-standards/naming.md「命名空间与目录」）。
///
/// 守的是两条**硬规则**，不是「目录逐层镜像命名空间」：
/// <list type="number">
/// <item><b>R1</b>：文件声明的命名空间必须以**所在程序集名**打头 —— 不得跨程序集占名。
///   （反例：曾有 <c>Tnzi.EFCore/Data/IdGenerators/IEntityIdGenerator.cs</c> 声明
///   <c>Tnzi.Data.IdGenerators</c>，让人以为该类型在核心 <c>Tnzi</c> 里。）</item>
/// <item><b>R2</b>：**一级目录** = 一个命名空间单元，即 <c>{Assembly}/{Dir}/**</c> 下的文件
///   必须声明 <c>{Assembly}.{Dir}</c>（或其子命名空间）。
///   （反例：曾有 <c>Tnzi.EFCore/DocumentNumbering/*.cs</c> 声明 <c>Tnzi.EFCore</c>，
///   而同项目另外 12 个一级目录都遵守此规则。）</item>
/// </list>
///
/// 另守 R3 里消费方真正会导入的那一处：<b><c>Services/Interfaces/</c> 不产生子命名空间</b>
/// （<see cref="ServiceInterfaces_StayInTheServicesNamespace"/>）。接口是消费方注入的类型，
/// 多一层 <c>.Interfaces</c> 就是每个消费方多写一行 using。
///
/// ★**其余二级目录不检查**：<c>Entities/Configs/</c> 与 <c>Controllers/Admin/</c> 的既成惯例是
/// 目录即子命名空间（<c>.Entities.Configs</c>、<c>.Controllers.Admin</c>），两种写法并存且都合法，
/// 详见 naming.md 的 R3。
///
/// 扫描范围见 <see cref="ScanRoots"/>（<c>src/</c> 与 <c>tools/</c>），
/// 由 <see cref="Scan_CoversEveryRoot"/> 守着「扫描面没塌」。
/// </summary>
public class NamespaceConventionTests
{
    private static readonly Regex NamespaceRegex =
        new(@"^\s*namespace\s+([A-Za-z0-9_.]+)\s*[;{]", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// 经登记的例外（相对仓库根，正斜杠）。新增例外必须在此登记并写明理由，
    /// 否则门禁会红 —— 这正是它存在的意义：错位可以有，但必须是**有理由且被记录**的。
    /// </summary>
    private static readonly Dictionary<string, string> RegisteredExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 入口点刻意放在根命名空间：消费应用的 Program.cs 只写
        // `await TnziApp.RunAsync<StartupModule>(args);`，不必先 using Tnzi.AspNetCore。
        // 2026-07-31 经确认保持现状。
        ["src/Tnzi.AspNetCore/TnziApp.cs"] = "框架入口点，刻意占用根命名空间 Tnzi 以简化消费方 Program.cs",
    };

    [Fact]
    public void Namespace_MustStartWithOwningAssemblyName()
    {
        var repoRoot = RepoRoot.Locate();

        var violations = new List<string>();

        foreach (var (proj, file, rel, ns) in EnumerateDeclarations(repoRoot))
        {
            if (RegisteredExceptions.ContainsKey(rel)) continue;

            if (ns != proj && !ns.StartsWith(proj + ".", StringComparison.Ordinal))
            {
                violations.Add($"{rel}\n      声明 {ns}，但它属于程序集 {proj}");
            }
        }

        Assert.True(violations.Count == 0,
            "以下文件声明了不属于本程序集的命名空间（违反 R1）。跨程序集占名会让消费方\n"
            + "以为类型在别的包里，也会在两个包同时加载时产生歧义：\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void TopLevelDirectory_MustMapToNamespaceUnit()
    {
        var repoRoot = RepoRoot.Locate();

        var violations = new List<string>();

        foreach (var (proj, file, rel, ns) in EnumerateDeclarations(repoRoot))
        {
            if (RegisteredExceptions.ContainsKey(rel)) continue;

            var segments = rel.Split('/');
            // segments: {root} / {Proj} / [dir1 / dir2 / ...] / File.cs   （root = src 或 tools）
            if (segments.Length < 4) continue;      // 项目根下的文件 -> 允许根命名空间
            var topDir = segments[2];

            var expectedPrefix = $"{proj}.{topDir}";
            if (ns != expectedPrefix && !ns.StartsWith(expectedPrefix + ".", StringComparison.Ordinal))
            {
                violations.Add($"{rel}\n      声明 {ns}，但一级目录 {topDir}/ 要求 {expectedPrefix}[.*]");
            }
        }

        Assert.True(violations.Count == 0,
            "以下文件违反 R2（一级目录 = 一个命名空间单元）。要么把命名空间改对，\n"
            + "要么把文件移到与其命名空间相符的目录：\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void ServiceInterfaces_StayInTheServicesNamespace()
    {
        var repoRoot = RepoRoot.Locate();

        var scanned = 0;
        var violations = new List<string>();

        foreach (var (proj, file, rel, ns) in EnumerateDeclarations(repoRoot))
        {
            if (RegisteredExceptions.ContainsKey(rel)) continue;

            var segments = rel.Split('/');
            // {root} / {Proj} / Services / Interfaces / File.cs
            if (segments.Length != 5 || segments[2] != "Services" || segments[3] != "Interfaces") continue;

            scanned++;
            var expected = $"{proj}.Services";
            if (ns != expected)
            {
                violations.Add($"{rel}\n      声明 {ns}，应为 {expected}");
            }
        }

        // 找不到违规即通过的断言，要先证明它真的看到了文件：目录改名或扫描坏掉时这里当场红，
        // 而不是一片绿。
        Assert.True(scanned > 0, "一个 Services/Interfaces/ 下的文件都没扫到，是扫描坏了而不是没有违规");

        Assert.True(violations.Count == 0,
            "以下服务接口声明了子命名空间（违反 R3）。接口是消费方注入的类型，\n"
            + "它必须与实现同在 {Module}.Services，否则每个消费方都要多写一行 using：\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>
    /// 每个<b>在场的</b>扫描根都必须真的扫到文件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// R1/R2 两条都是「找不到违规即通过」，所以**扫描面塌掉与没有违规不可区分**：
    /// 路径写错、目录改名、枚举抛异常，表现全都是一片绿。这条守卫把两者分开 ——
    /// 它是这个仓库反复兑现过的教训（模块依赖门禁曾长年只审 10/54 个模块而全绿）。
    /// </para>
    /// <para>
    /// ★ 「在场」这个限定是 2026-08-30 为公开镜像加的。镜像只投影 <c>src/</c> +
    /// <c>tests/</c> 与几个根文件，<c>tools/</c> 整个不去，于是这条断言在那边**必红**，
    /// 而红的原因与代码质量无关。判据因此收窄成「每个<b>存在</b>的根都要扫到文件，
    /// 且必需根必须存在」：<c>src/</c> 扫空仍然当场红，镜像里缺席的 <c>tools/</c> 安静跳过。
    /// </para>
    /// <para>
    /// 代价写在明处：私有仓里 <c>tools/</c> 被整个删掉或改名，这条不再变红。那是 git
    /// 层面一眼可见的事件，与「扫描面悄悄塌掉」不是一类风险 —— 后者恰恰无声，才要它守。
    /// </para>
    /// </remarks>
    [Fact]
    public void Scan_CoversEveryRoot()
    {
        var repoRoot = RepoRoot.Locate();

        var byRoot = EnumerateDeclarations(repoRoot)
            .GroupBy(d => d.Rel.Split('/')[0], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var missingRequired = RequiredScanRoots
            .Where(r => !Directory.Exists(Path.Combine(repoRoot, r)))
            .ToList();

        Assert.True(missingRequired.Count == 0,
            $"以下扫描根在仓库里不存在，是路径写错或目录改名: {string.Join(", ", missingRequired)}");

        var empty = ScanRoots
            .Where(r => Directory.Exists(Path.Combine(repoRoot, r)))
            .Where(r => !byRoot.ContainsKey(r) || byRoot[r] == 0)
            .ToList();

        Assert.True(empty.Count == 0,
            $"以下扫描根一个命名空间声明都没扫到，是扫描坏了而不是没有违规: {string.Join(", ", empty)}\n"
            + $"实际扫到: {string.Join(", ", byRoot.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    [Fact]
    public void RegisteredExceptions_AreNotStale()
    {
        var repoRoot = RepoRoot.Locate();

        var stale = RegisteredExceptions.Keys
            .Where(rel => !File.Exists(Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        Assert.True(stale.Count == 0,
            "以下登记的例外所指文件已不存在，应从 RegisteredExceptions 移除:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// 扫描根。<c>tools/</c> 于 2026-08-15 工具链合并回本仓时加入：它下面的
    /// <c>Tnzi.Cli</c> / <c>Tnzi.Mcp</c> / <c>Tnzi.Scaffold</c> 同样是 <c>Tnzi.*</c> 程序集，
    /// R1（不得跨程序集占名）对它们一样成立。合并当天核对过三者都符合 R1/R2，
    /// 所以纳入门禁的成本是零 —— 而不纳入的代价，那次合并刚好给出了实例：
    /// 分居两仓、没有门禁盯着的东西（模板、笔记、MCP 能力清单的数字）全都漂了。
    /// </summary>
    private static readonly string[] ScanRoots = ["src", "tools"];

    /// <summary>
    /// 必须在场的扫描根。<c>tools/</c> 刻意不在其中 —— 公开镜像不投影它，
    /// 理由见 <see cref="Scan_CoversEveryRoot"/>。
    /// </summary>
    private static readonly string[] RequiredScanRoots = ["src"];

    private static IEnumerable<(string Proj, string File, string Rel, string Ns)> EnumerateDeclarations(string repoRoot)
    {
        foreach (var root in ScanRoots)
        foreach (var projDir in EnumerateProjectDirs(repoRoot, root))
        {
            var proj = Path.GetFileName(projDir);
            // Tnzi.UI 是前端 pnpm monorepo，不含 C# 项目
            if (!proj.StartsWith("Tnzi", StringComparison.Ordinal) || proj == "Tnzi.UI") continue;

            foreach (var file in EnumerateCsFiles(projDir))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("GlobalUsings", StringComparison.OrdinalIgnoreCase)) continue;

                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }

                var m = NamespaceRegex.Match(text);
                if (!m.Success) continue;   // 无命名空间声明（如仅含 assembly 特性）

                var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                yield return (proj, file, rel, m.Groups[1].Value);
            }
        }
    }

    /// <summary>
    /// 缺失的扫描根返回空，判定留给 <see cref="Scan_CoversEveryRoot"/>，不在这里静默跳过 ——
    /// 必需根缺席在那里是错误，可选根（<c>tools/</c>，公开镜像不投影）缺席才是跳过。
    /// </summary>
    private static IEnumerable<string> EnumerateProjectDirs(string repoRoot, string root)
    {
        var dir = Path.Combine(repoRoot, root);
        return Directory.Exists(dir) ? Directory.GetDirectories(dir) : [];
    }

    /// <summary>
    /// 手动递归枚举 .cs 文件，跳过 bin/obj 及 reparse point（junction/symlink），
    /// 避免 cloud-sync 链接造成的 AllDirectories 无限递归。
    /// </summary>
    private static IEnumerable<string> EnumerateCsFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files;
            string[] subDirs;
            try
            {
                files = Directory.GetFiles(dir, "*.cs");
                subDirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var f in files)
                yield return f;

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (name is "bin" or "obj" or "node_modules" or ".git")
                    continue;
                if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
                stack.Push(sub);
            }
        }
    }
}
