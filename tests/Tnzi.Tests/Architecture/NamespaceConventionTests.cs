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
/// ★**刻意不检查二级及以下目录**：那些是**开发期分类目录**，按 R3 不产生子命名空间。
/// <c>Data/Filtering/</c>、<c>Extensions/Primitives/</c>、<c>Controllers/Admin/</c> 里的文件
/// 仍属父命名空间 —— 命名空间是消费方的**导入单位**，不该让消费方为「我们怎么分文件」
/// 多写 N 行 using。
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
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return;   // 打包/隔离环境跳过，不误报

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
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return;

        var violations = new List<string>();

        foreach (var (proj, file, rel, ns) in EnumerateDeclarations(repoRoot))
        {
            if (RegisteredExceptions.ContainsKey(rel)) continue;

            var segments = rel.Split('/');
            // segments: src / {Proj} / [dir1 / dir2 / ...] / File.cs
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
    public void RegisteredExceptions_AreNotStale()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return;

        var stale = RegisteredExceptions.Keys
            .Where(rel => !File.Exists(Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        Assert.True(stale.Count == 0,
            "以下登记的例外所指文件已不存在，应从 RegisteredExceptions 移除:\n  " + string.Join("\n  ", stale));
    }

    private static IEnumerable<(string Proj, string File, string Rel, string Ns)> EnumerateDeclarations(string repoRoot)
    {
        var srcDir = Path.Combine(repoRoot, "src");
        foreach (var projDir in Directory.GetDirectories(srcDir))
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

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Tnzi.NET.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
