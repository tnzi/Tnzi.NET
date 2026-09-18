using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 测试套件里的递归目录枚举必须会停 —— 在<b>前端已安装</b>的机器上也要停。
/// </summary>
/// <remarks>
/// <para>
/// <b>这不是假想。</b><c>Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)</c>
/// 在 Windows 上跟随 junction 与 symlink，而 <c>src/Tnzi.UI</c> 是 pnpm 工作区：
/// 它的 <c>node_modules</c> 顶层就有 13 个 junction 指进 <c>.pnpm</c>，<c>.pnpm</c> 自己又嵌在
/// <c>node_modules</c> 底下，实测能走出
/// <c>@tnzi/ui-ai/playground/node_modules/@tnzi/ui-ai/playground/node_modules/@tnzi/ui-ai/…</c>
/// 这样的自指路径。以仓库根或 <c>src/</c> 为根的裸递归因此<b>永不终止</b>。
/// </para>
/// <para>
/// ★★★ <b>症状把人指到完全错误的地方</b>：测试宿主涨过 3 GB 然后卡死，
/// <b>一条测试结果都打不出来</b> —— 读起来像测试自己内存泄漏。加 <c>--filter</c> 也没用，
/// 发现阶段就挂了，筛选还没轮到。整个套件在开发机上因此跑不出任何信号。
/// </para>
/// <para>
/// 本文件锁三件事：<br/>
/// ① <see cref="RepoScan"/> 面对真的重解析点环会停（行为，合成语料，处处都跑）；<br/>
/// ② 仍在用裸递归的那批门禁，它们的扫描根底下<b>没有</b>目录重解析点
///    （结构，防的是「下一个 pnpm 依赖把链接森林长到别处去」）；<br/>
/// ③ 裸递归的调用点清单没有悄悄变长（存量，逼新增的人来这里做一次判断）。
/// </para>
/// <para>
/// ⚠️ <b>③盖不住手写的递归。</b>它按 <c>SearchOption.AllDirectories</c> /
/// <c>RecurseSubdirectories</c> 这两个词判定，而自己压栈的 <c>while</c> 循环没有这种特征词。
/// 本仓已有一处这样的手写遍历（<c>NamespaceConventionTests.EnumerateCsFiles</c>），
/// 它<b>做对了</b>（跳重解析点 + 按名字剪枝），但下一处未必。
/// 那种写法只能靠②在它的扫描根上兜住 —— 而②只认得下面列出的根。
/// </para>
/// <para>
/// 说到底这三条锁的是「今天已知的形态」，不是「所有可能的写法」。
/// 真正的防线是别自己写遍历：<see cref="RepoScan"/> 存在就是为了不必再写第五份。
/// </para>
/// <para>
/// ★ <b>为什么不干脆把 16 处全改掉</b>：<see cref="RepoScan"/> 的名字剪枝
/// （<c>node_modules</c>/<c>bin</c>/<c>obj</c>/<c>.git</c>/<c>dist</c>）是一条<b>仓库扫描策略</b>，
/// 不是通用枚举策略。自建临时目录的测试套上它会安静漏文件 —— 有一个正是在临时目录里造了
/// <c>.git</c> 再删整棵树。根下面本来就没有链接森林的走法不该被一次性改掉，
/// 那既不修复什么，又会给两条清理路径埋一个新坑。
/// </para>
/// </remarks>
public class DirectoryWalkTerminationTests
{
    /// <summary>裸递归枚举的两种写法。</summary>
    private static readonly Regex RawRecursiveWalk = new(
        @"SearchOption\.AllDirectories|RecurseSubdirectories",
        RegexOptions.Compiled);

    /// <summary>
    /// 不计入存量清单的文件。
    /// </summary>
    /// <remarks>
    /// <c>RepoScan.cs</c> 是<b>安全实现本身</b>，它当然含有 <c>RecurseSubdirectories = true</c>；
    /// 本文件含有上面那条正则与各处说明文字。两者都会匹配到自己，
    /// 与 <c>HardcodedPathConventionTests</c> 抓到自己的 XML 注释是同一个形态。
    /// </remarks>
    private static readonly string[] NotInTheInventory = ["RepoScan.cs", "DirectoryWalkTerminationTests.cs"];

    /// <summary>
    /// 仍在用裸递归的调用点。
    /// </summary>
    /// <remarks>
    /// 每条都附了扫描根。判据只有一条：<b>那个根底下不会出现目录重解析点</b>。
    /// <c>(temp)</c> 是测试自建并自行删除的临时目录，不是仓库路径，
    /// 由造它的测试自己负责，不在下面 ②的检查面里。
    /// </remarks>
    private static readonly string[] ExpectedRawWalkFiles =
    [
        "Tnzi.Architecture.Tests/DocsDisclosureTests.cs",                // docs/
        "Tnzi.Architecture.Tests/FrontendApiScanner.cs",                 // src/Tnzi.UI/packages/core/src/services
        "Tnzi.Architecture.Tests/IconManifestTests.cs",                  // src/Tnzi.UI/packages/{pkg}/src
        "Tnzi.Architecture.Tests/SourceScanner.cs",                      // src/{assembly}[/Controllers]
        "Tnzi.Architecture.Tests/WebsiteModuleCatalogTests.cs",          // website/src
        "Tnzi.Mcp.Tests/DocFreshnessSourceClassificationTests.cs",       // (temp) 自建临时目录的清理
        "Tnzi.Storage.Tests/StorageKeyCallSiteGateTests.cs",             // src/Tnzi.Storage{,.Workspace,.Cloud}
        "Tnzi.Tests/Architecture/CliAgentRedLineTests.cs",               // src/Tnzi.AI/Middleware、src/Tnzi.AI.Cli
    ];

    /// <summary>
    /// ① <see cref="RepoScan"/> 走到一个自指的重解析点环里也会停，并且照常出结果。
    /// </summary>
    /// <remarks>
    /// ★ <b>刻意用合成语料而不是仓库自己的 <c>node_modules</c></b>：后者在没跑过
    /// <c>pnpm install</c> 的机器（干净 CI 检出）上根本不存在，那样这条测试会退化成
    /// 一条什么都没验证的绿测试 —— 而它要防的恰恰是「看起来通过了」。
    /// </remarks>
    [Fact]
    public void RepoScan_Terminates_On_A_Self_Referencing_Reparse_Point()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"tnzi-walk-gate-{Guid.NewGuid():N}");
        var real = Path.Combine(sandbox, "real");
        var nested = Path.Combine(real, "nested");

        try
        {
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(real, "top.cs"), "// top");
            File.WriteAllText(Path.Combine(nested, "deep.cs"), "// deep");

            // real/loop -> real，于是 real/loop/loop/loop/… 无穷。
            CreateDirectoryLink(Path.Combine(real, "loop"), real);

            // 先证明前提成立：这确实是一个会让裸递归走不完的环。
            var reparsePoints = RepoScan.FindDirectoryReparsePointsIn(sandbox);
            reparsePoints.Count.ShouldBe(1,
                "合成语料没造出重解析点，那么下面「它会停」证明不了任何事。");

            var stopwatch = Stopwatch.StartNew();
            var found = RepoScan.EnumerateFilesIn(real, "*.cs")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            stopwatch.Stop();

            found.ShouldBe(["deep.cs", "top.cs"],
                "RepoScan 应当照常返回真实文件 —— 不跟随重解析点不等于少扫真目录。"
                + "只跳过链接本身，链接指向的那棵真树该由它自己的路径扫到。");

            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30),
                "枚举没有在合理时间内结束，说明它跟着链接走进了无穷路径。");
        }
        finally
        {
            TryDeleteSandbox(sandbox);
        }
    }

    /// <summary>
    /// ② 裸递归门禁的扫描根底下不得出现目录重解析点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这条才是防「下一个 pnpm 依赖悄悄把它带回来」的那道闸。上面那批调用点今天是安全的，
    /// <b>唯一的理由</b>就是它们的根底下一个目录链接都没有 —— 那是一条实测出来的事实，
    /// 在此之前没有任何东西守着它。谁往 <c>tools/</c>、<c>docs/</c> 或某个模块目录里
    /// 装出第二个 pnpm 工作区，这里会当场红，而不是留给下一个人去撞 3 GB 卡死。
    /// </para>
    /// <para>
    /// ★ <b>只看目录重解析点</b>：文件符号链接不会让遍历变长。本仓的
    /// <c>src/*/CLAUDE.md</c> 有 60 多个 cloud-sync 符号链接，把它们报成问题只会淹掉真信号。
    /// </para>
    /// </remarks>
    [Fact]
    public void No_Raw_Walk_Scan_Root_Contains_A_Directory_Reparse_Point()
    {
        var checkedRoots = 0;
        var offenders = new List<string>();

        foreach (var root in ScanRootsThatMustStayLinkFree())
        {
            checkedRoots++;
            foreach (var reparsePoint in RepoScan.FindDirectoryReparsePoints(root))
                offenders.Add($"{reparsePoint}  （落在扫描根 {root} 底下）");
        }

        // 下界守卫：根的收集一旦写错，上面的断言会一起假绿。src/ 下光模块目录就有 50+。
        checkedRoots.ShouldBeGreaterThan(40,
            $"只检查了 {checkedRoots} 个扫描根 —— 是收集逻辑坏了，不是仓库真的这么小。");

        offenders.ShouldBeEmpty(
            "有目录重解析点长在了「还在用裸递归」的门禁的扫描根底下。"
            + "那些门禁会跟着链接走进无穷路径：测试宿主涨到几 GB 后卡死，"
            + "而且一条测试结果都打不出来，看起来像测试自己内存泄漏。"
            + $"{Environment.NewLine}把对应的门禁改用 RepoScan.EnumerateFiles，"
            + $"或者把这片链接森林挪出扫描根。{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// ③ 裸递归的调用点清单没有悄悄变长。
    /// </summary>
    /// <remarks>
    /// ②只认得<b>已知的</b>那批根。有人新写一处以 <c>src/</c> 或仓库根为根的裸递归时，
    /// ②看不见它，而它会让整个套件重新跑不出结果。这条清单逼新增的人来这里做一次判断：
    /// 扫的是仓库源码树就改用 <see cref="RepoScan"/>，是自建临时目录就把自己加进清单。
    /// <para>
    /// 与 <c>ModuleInventoryTests</c> / <c>PermissionProviderInventoryTests</c> 同一个存量锁形态。
    /// </para>
    /// </remarks>
    [Fact]
    public void The_Inventory_Of_Raw_Recursive_Walks_Has_Not_Grown()
    {
        var actual = RepoScan.EnumerateFiles("tests", "*.cs")
            .Where(file => !NotInTheInventory.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            .Where(ContainsRawWalkOutsideComments)
            .Select(file => Path.GetRelativePath(Path.Combine(RepoRoot.Locate(), "tests"), file).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var added = actual.Except(ExpectedRawWalkFiles, StringComparer.Ordinal).ToList();

        // 「陈旧」只认还在磁盘上却不再含裸递归的文件。公开镜像会把读 docs/、website/、tools/
        // 的测试文件整个排掉（/publish-public 的排除列表），那些条目在镜像里是缺席，
        // 不是收缩；而一条指向已删文件的条目挡不住任何新增（新增是另一条路径，落在 added 里），
        // 把镜像跑红换来的只是这道门禁被整体忽略。
        var testsRoot = Path.Combine(RepoRoot.Locate(), "tests");
        var removed = ExpectedRawWalkFiles
            .Except(actual, StringComparer.Ordinal)
            .Where(path => File.Exists(Path.Combine(testsRoot, path)))
            .ToList();

        added.ShouldBeEmpty(
            "新增了裸递归目录枚举。它跟随 junction/symlink：只要扫描根够到 src/Tnzi.UI 的 pnpm 工作区，"
            + "整个测试宿主会卡死且一条结果都打不出来。"
            + $"{Environment.NewLine}扫仓库源码树请改用 RepoScan.EnumerateFiles(\"src\", \"*.cs\")；"
            + "只扫自建的临时目录则把文件加进 ExpectedRawWalkFiles，并写明扫描根。"
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, added)}");

        removed.ShouldBeEmpty(
            "ExpectedRawWalkFiles 里的文件已经不含裸递归了 —— 清单该跟着收缩，"
            + $"留着会让下一次真正的新增被这条陈旧条目掩盖。{Environment.NewLine}"
            + string.Join(Environment.NewLine, removed));
    }

    /// <summary>
    /// 必须保持「零目录链接」的扫描根。
    /// </summary>
    /// <remarks>
    /// <c>src/</c> 的每个一级子目录（<c>Tnzi.UI</c> 除外，前端工作区就住在那里），
    /// 加上前端各包的 <c>src/</c> 与 <c>docs/</c>、<c>website/src</c>。
    /// <para>
    /// ★ <c>docs/</c> 与 <c>website/</c> <b>不投影到公开镜像</b>，在那边不存在；
    /// 存在才检查是对的 —— 它们缺席时，靠它们的那两条门禁本来也跑不了
    /// （<c>DocsDisclosureTests</c> 对目录缺失是直接失败的）。
    /// 而 <c>src/</c> 一定存在，所以那部分不设这个条件。
    /// </para>
    /// </remarks>
    private static IEnumerable<string> ScanRootsThatMustStayLinkFree()
    {
        var repoRoot = RepoRoot.Locate();

        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(repoRoot, "src")))
        {
            var name = Path.GetFileName(directory);
            if (!string.Equals(name, "Tnzi.UI", StringComparison.OrdinalIgnoreCase))
                yield return $"src/{name}";
        }

        var packages = Path.Combine(repoRoot, "src", "Tnzi.UI", "packages");
        if (Directory.Exists(packages))
        {
            foreach (var package in Directory.EnumerateDirectories(packages))
            {
                var name = Path.GetFileName(package);
                if (Directory.Exists(Path.Combine(package, "src")))
                    yield return $"src/Tnzi.UI/packages/{name}/src";
            }
        }

        foreach (var optional in new[] { "docs", "website/src" })
        {
            if (Directory.Exists(Path.Combine(repoRoot, optional.Replace('/', Path.DirectorySeparatorChar))))
                yield return optional;
        }
    }

    /// <summary>
    /// 文件里是否有<b>不在注释里</b>的裸递归写法。
    /// </summary>
    /// <remarks>
    /// 改用 <see cref="RepoScan"/> 的那几处，注释里会写明「不走 SearchOption.AllDirectories 的理由」——
    /// 那正是这些注释存在的意义，不该反过来被自己拦下。判据取整行起始，
    /// 覆盖 <c>//</c>、<c>///</c> 与块注释续行的 <c>*</c>，与 <c>HardcodedPathConventionTests</c> 同法。
    /// </remarks>
    private static bool ContainsRawWalkOutsideComments(string file)
    {
        foreach (var line in File.ReadLines(file))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
                continue;

            if (RawRecursiveWalk.IsMatch(line))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 造一个目录重解析点。
    /// </summary>
    /// <remarks>
    /// Windows 上用 <c>mklink /J</c> 造 junction：<b>它不需要提权</b>，
    /// 而 <see cref="Directory.CreateSymbolicLink"/> 在没开开发者模式的 Windows 上要管理员权限，
    /// 会让这条门禁在普通开发机上跑不起来。junction 也正是 pnpm 实际用的那种链接。
    /// 非 Windows 上符号链接本就不需要特权，直接用框架 API。
    /// </remarks>
    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"造不出 junction（mklink 退出码 {process.ExitCode}）：{process.StandardError.ReadToEnd()}"
                + "。这里刻意抛出而不是跳过 —— 造不出环的话，这条门禁什么也没验证。");
        }
    }

    /// <summary>
    /// 删掉合成语料。
    /// </summary>
    /// <remarks>
    /// ⚠️ 必须<b>先删链接本身</b>再递归删。<c>Directory.Delete(recursive: true)</c> 在 Windows 上
    /// 不会跟进 junction 去删目标内容，但这里的 junction 指向的正是它自己的父目录，
    /// 先摘掉它最省心，也免得任何一层实现差异把真目录连带删掉。
    /// </remarks>
    private static void TryDeleteSandbox(string sandbox)
    {
        try
        {
            var loop = Path.Combine(sandbox, "real", "loop");
            if (Directory.Exists(loop))
                Directory.Delete(loop);

            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
        catch (IOException) { /* 临时目录，留给系统清 */ }
        catch (UnauthorizedAccessException) { /* 同上 */ }
    }
}
