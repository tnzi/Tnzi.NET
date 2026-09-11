using System.IO.Enumeration;

namespace Tnzi.TestBase;

/// <summary>
/// 源码扫描类门禁的目录枚举器 —— 递归但<b>永不跟随重解析点</b>，因此一定会停。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是一次实测到的「测试跑不出任何结果」。<c>Directory.EnumerateFiles(root, pattern,
/// SearchOption.AllDirectories)</c> 在 Windows 上<b>跟随 junction 与 symlink</b>
/// （该重载映射到 <c>AttributesToSkip = 0</c> 的枚举选项，连隐藏/系统属性都不跳，更不会跳重解析点）。
/// 而 <c>src/Tnzi.UI</c> 是一个 pnpm 工作区：它的 <c>node_modules</c> 光顶层就有 13 个 junction
/// 指进 <c>.pnpm</c>，<c>.pnpm</c> 自己又嵌在 <c>node_modules</c> 底下，于是走出实测到的这种环 ——
/// <c>@tnzi/ui-ai/playground/node_modules/@tnzi/ui-ai/playground/node_modules/@tnzi/ui-ai/…</c>
/// 一路自指下去。
/// </para>
/// <para>
/// ★★★ <b>症状会把人指到完全错误的地方。</b>路径越走越长、每一条都留在枚举状态里，
/// 于是测试宿主涨过 3 GB 然后卡死，<b>一条测试结果都打不出来</b> —— 读起来像测试自己内存泄漏，
/// 而不像目录枚举走不完。加 <c>--filter</c> 也没用：发现阶段就挂了，筛选还没轮到。
/// 消费方与本仓各自在这上面丢过好几轮排查。
/// </para>
/// <para>
/// ★★ <b>为什么本机 PowerShell 试出来「能跑完」是假象。</b>Windows PowerShell 5.1 跑在
/// .NET Framework 上，受 260 字符 MAX_PATH 限制，环走到 264 字符就自己报错退出，
/// 看着像「慢了 19 倍但会停」。.NET（含 .NET 10 测试宿主）内部自动加 <c>\\?\</c> 扩展前缀，
/// <b>不受 MAX_PATH 约束，所以是真的不停</b>。拿 PowerShell 5.1 复现这个 bug 会得出相反结论。
/// </para>
/// <para>
/// ★ <b>本类型只用来扫仓库源码树，不是通用的目录枚举器。</b>下面的名字剪枝
/// （<c>node_modules</c> / <c>bin</c> / <c>obj</c> / <c>.git</c> / <c>dist</c>）是一条
/// <b>仓库扫描策略</b>。拿它去清理自建的临时目录会安静地漏掉文件 —— 例如某些测试会在临时目录里
/// 造一个 <c>.git</c> 再删掉整棵树，走这里就正好跳过它要处理的那批文件。
/// 管自己临时目录的测试<b>不该</b>用本类型，那种根下面本来也没有重解析点森林。
/// </para>
/// <para>
/// ⚠️ <b>重解析点<i>文件</i>也一并跳过</b>，这对 <c>*.cs</c> / <c>*.csproj</c> 类扫描没有代价
/// （实测本仓只有 <c>*.md</c> 是符号链接：<c>src/*/CLAUDE.md</c> 那 62 个 cloud-sync 链接，
/// 加根 <c>CLAUDE.md</c> 与 <c>tools/</c> 下 3 个）。但这意味着<b>拿本类型扫 <c>src/</c> 下的
/// <c>*.md</c> 会安静地漏掉全部 CLAUDE.md</b>。真要扫那些文件，别走这里。
/// </para>
/// </remarks>
public static class RepoScan
{
    /// <summary>
    /// 不进入的目录名。
    /// </summary>
    /// <remarks>
    /// 与 <c>RuntimeFloorConventionTests</c> 原有的 <c>SkippedSegments</c> 逐字相同 ——
    /// 刻意不趁机扩充，免得这次改动顺手改掉某条门禁的扫描面。
    /// <para>
    /// ★ 名字剪枝是<b>皮带</b>，跳重解析点才是<b>背带</b>：没有链接的检出（CI 上
    /// <c>pnpm install --node-linker=hoisted</c>、或从压缩包展开的依赖树）里
    /// <c>node_modules</c> 是实打实的目录，跳重解析点救不了，得靠名字。
    /// 反过来，链接指向仓库外或换了个名字时，名字救不了，得靠跳重解析点。两条都要。
    /// </para>
    /// </remarks>
    private static readonly string[] PrunedDirectoryNames = ["node_modules", "bin", "obj", ".git", "dist"];

    /// <summary>
    /// 枚举仓库内某个目录下（含子目录）匹配 <paramref name="searchPattern"/> 的文件，返回绝对路径。
    /// </summary>
    /// <param name="relativeRoot">相对仓库根的目录，用 <c>/</c> 分隔；<c>""</c> 或 <c>"."</c> 表示仓库根。</param>
    /// <param name="searchPattern">Win32 通配模式，语义与 <c>Directory.EnumerateFiles</c> 一致。</param>
    /// <exception cref="DirectoryNotFoundException">目录不存在。</exception>
    /// <remarks>
    /// 目录不存在时<b>抛出</b>而不是返回空序列 —— 与 <see cref="RepoRoot.Locate"/> 同一个理由：
    /// 扫不到东西的门禁会全绿，「门禁没跑」和「门禁通过」必须是两个可区分的观测结果。
    /// </remarks>
    public static IEnumerable<string> EnumerateFiles(string relativeRoot, string searchPattern)
        => EnumerateFilesIn(ResolveRoot(relativeRoot), searchPattern, relativeRoot);

    /// <summary>
    /// 同 <see cref="EnumerateFiles"/>，但扫描根是一个<b>绝对路径</b>。
    /// </summary>
    /// <remarks>
    /// 底层入口。门禁请优先用 <see cref="EnumerateFiles"/> 的仓库相对重载 ——
    /// 相对路径本身就说明了「扫的是仓库源码树」，而这条剪枝策略只在那个前提下成立。
    /// 本重载存在是为了让 <c>DirectoryWalkTerminationTests</c> 能在临时目录里造一个真的
    /// 重解析点环来证明它会停：拿仓库自己的 <c>node_modules</c> 当被测对象，
    /// 在没跑过 <c>pnpm install</c> 的机器上会退化成一条什么也没验证的绿测试。
    /// </remarks>
    public static IEnumerable<string> EnumerateFilesIn(string directory, string searchPattern)
        => EnumerateFilesIn(directory, searchPattern, directory);

    private static IEnumerable<string> EnumerateFilesIn(string root, string searchPattern, string displayName)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"源码扫描门禁的扫描根不存在：{displayName}（解析为 {root}）。"
                + "这里刻意抛出而不是返回空序列 —— 扫不到文件的门禁会全绿，与真的通过无法区分。");
        }

        // 大小写敏感性对齐 Directory.EnumerateFiles 的平台默认：Linux 敏感，Windows/macOS 不敏感。
        var ignoreCase = !OperatingSystem.IsLinux();

        return new FileSystemEnumerable<string>(
            root,
            static (ref FileSystemEntry entry) => entry.ToFullPath(),
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                // 属性过滤刻意留空：跳过哪些条目全部写在下面两个谓词里，
                // 免得「跳重解析点」这条关键行为藏在一个默认值里被人顺手改掉。
                AttributesToSkip = 0,
                IgnoreInaccessible = true,
            })
        {
            ShouldRecursePredicate = static (ref FileSystemEntry entry) =>
                !IsReparsePoint(ref entry) && !IsPrunedDirectoryName(entry.FileName),

            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory
                && !IsReparsePoint(ref entry)
                && FileSystemName.MatchesWin32Expression(searchPattern, entry.FileName, ignoreCase),
        };
    }

    /// <summary>
    /// 仓库内某个目录下（含子目录）的全部<b>目录</b>重解析点，返回相对仓库根的路径。
    /// </summary>
    /// <remarks>
    /// 给门禁用：判断某个扫描根「能不能走出无穷路径」。
    /// <para>
    /// ★ 与 <see cref="EnumerateFiles"/> 的两处刻意不同：①<b>不按名字剪枝</b> ——
    /// <c>node_modules</c> 正是要找的东西，剪掉就什么也发现不了；
    /// ②只报<b>目录</b>重解析点 —— 文件符号链接不会让遍历变长，
    /// 把 <c>src/*/CLAUDE.md</c> 那 60 多个 cloud-sync 链接报成问题只会淹掉真信号。
    /// </para>
    /// <para>
    /// 本方法自己也不跟随重解析点，所以它在这棵有环的树上同样会停。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> FindDirectoryReparsePoints(string relativeRoot)
    {
        var repoRoot = RepoRoot.Locate();

        return FindDirectoryReparsePointsIn(ResolveRoot(relativeRoot), relativeRoot)
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .ToList();
    }

    /// <summary>
    /// 同 <see cref="FindDirectoryReparsePoints"/>，但扫描根是<b>绝对路径</b>，返回的也是绝对路径。
    /// </summary>
    public static IReadOnlyList<string> FindDirectoryReparsePointsIn(string directory)
        => FindDirectoryReparsePointsIn(directory, directory);

    private static IReadOnlyList<string> FindDirectoryReparsePointsIn(string root, string displayName)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"要检查的目录不存在：{displayName}（解析为 {root}）。");
        }

        var found = new FileSystemEnumerable<string>(
            root,
            static (ref FileSystemEntry entry) => entry.ToFullPath(),
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = 0,
                IgnoreInaccessible = true,
            })
        {
            ShouldRecursePredicate = static (ref FileSystemEntry entry) => !IsReparsePoint(ref entry),
            ShouldIncludePredicate = static (ref FileSystemEntry entry) =>
                entry.IsDirectory && IsReparsePoint(ref entry),
        };

        return found
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static string ResolveRoot(string relativeRoot)
    {
        var repoRoot = RepoRoot.Locate();

        return string.IsNullOrEmpty(relativeRoot) || relativeRoot == "."
            ? repoRoot
            : Path.Combine(repoRoot, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsReparsePoint(ref FileSystemEntry entry)
        => (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool IsPrunedDirectoryName(ReadOnlySpan<char> name)
    {
        foreach (var pruned in PrunedDirectoryNames)
        {
            if (name.Equals(pruned, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
