using System.IO;

namespace Tnzi.Tests.Architecture;

/// <summary>
/// 文件长度约定门禁：把项目自身的"单文件 &lt; 800 行"标准从散文升级为受测不变量。
/// 现有超标文件通过 allowlist 豁免（避免一次性大重构阻断 CI），但<b>禁止新增</b>超标文件，
/// 且 allowlist 中的文件不得继续增长。随着存量文件被增量重构，应从 allowlist 中移除对应条目。
/// </summary>
public class FileLengthConventionTests
{
    private const int MaxLines = 800;

    /// <summary>
    /// 现有超标文件豁免名单（相对仓库根，正斜杠）。新增文件不得进入此名单——应拆分。
    /// </summary>
    private static readonly HashSet<string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/Tnzi.Authorization/Services/FunctionAuthorizationService.cs",
        "src/Tnzi.Identity/Services/UserService.cs",
        "src/Tnzi.Storage/Services/FileStorageService.cs",
        "src/Tnzi.Identity/Services/AuthService.cs",
        "src/Tnzi.Identity/Services/OrganizationService.cs",
        "src/Tnzi.Redis/RedisCacheService.cs",
        "src/Tnzi.Identity/Services/IdentityPageService.cs",
        "src/Tnzi.EFCore/EfCoreRepository.cs",
        "src/Tnzi.Authorization/Services/DataAuthService.cs",
    };

    [Fact]
    public void NoSourceFileExceedsMaxLines_ExceptKnownAllowlist()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            // 打包/隔离环境下找不到源码树时跳过（不误报）
            return;
        }

        var srcDir = Path.Combine(repoRoot, "src");
        var violations = new List<string>();

        foreach (var file in EnumerateCsFiles(srcDir))
        {
            var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var lineCount = File.ReadLines(file).Count();
            if (lineCount > MaxLines && !Allowlist.Contains(rel))
            {
                violations.Add($"{rel} ({lineCount} lines)");
            }
        }

        Assert.True(violations.Count == 0,
            $"以下源文件超过 {MaxLines} 行且不在 allowlist 中，请拆分（或在确属合理时显式加入 allowlist 并说明原因）:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void Allowlist_HasNoStaleEntries()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            return;
        }

        // allowlist 中已不再超标的条目应被移除，保持名单收敛
        var stale = new List<string>();
        foreach (var rel in Allowlist)
        {
            var full = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                stale.Add($"{rel} (file missing)");
                continue;
            }
            if (File.ReadLines(full).Count() <= MaxLines)
            {
                stale.Add($"{rel} (now <= {MaxLines} lines — remove from allowlist)");
            }
        }

        Assert.True(stale.Count == 0,
            "以下 allowlist 条目已过时，应从 FileLengthConventionTests.Allowlist 移除:\n  "
            + string.Join("\n  ", stale));
    }

    /// <summary>
    /// 手动递归枚举 .cs 文件，跳过 bin/obj/node_modules 及 reparse point（junction/symlink），
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
                    continue; // 跳过 junction/symlink，防止无限递归
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
