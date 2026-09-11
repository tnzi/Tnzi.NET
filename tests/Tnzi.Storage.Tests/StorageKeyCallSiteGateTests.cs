using System.Text.RegularExpressions;
using Match = System.Text.RegularExpressions.Match;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 源码门禁：存储模块里每一处把键交给 provider 的调用，键都必须出自 <c>StorageKeyHelper</c>。
/// </summary>
/// <remarks>
/// <para>
/// <c>StorageKeyInvariantTests</c> 守的是**今天已知的**写路径；这一条守的是**明天新增的**：
/// 一条新写路径把调用方给的名字当键交给 provider，行为测试不会自动覆盖到它，而这里会红 ——
/// 因为它的第一个实参既不是 <c>StorageKeyHelper.*</c>，也不是一个由 <c>StorageKeyHelper.*</c>
/// 赋值的局部变量。
/// </para>
/// <para>
/// 判据刻意做成文本级而不是编译期：把键做成一个只能由工厂造出来的类型会改掉
/// <c>IFileStorage</c> 这个消费方可实现的扩展点的签名，代价落在每一个自定义 provider 上。
/// 而这里只约束框架自己的调用点 —— 那正是三次漂移发生的地方。
/// </para>
/// </remarks>
public class StorageKeyCallSiteGateTests
{
    /// <summary>被扫描的模块目录（相对仓库根）。</summary>
    private static readonly string[] ModuleRoots =
    [
        "src/Tnzi.Storage",
        "src/Tnzi.Storage.Workspace",
        "src/Tnzi.Storage.Cloud",
    ];

    /// <summary>
    /// 允许直接复用**已落库的**键的调用点：文件记录存在而物理文件丢失时按原键重传。
    /// 那个键当初就是生成出来的，重传不该给它换名字（否则记录与旧路径的对应关系就断了）。
    /// </summary>
    private static readonly HashSet<(string File, string Argument)> ReuseOfAStoredKey =
    [
        ("FileStorageService.cs", "existing.FileName"),
    ];

    private static readonly Regex CallSite = new(@"\.UploadAsync\(", RegexOptions.Compiled);

    [Fact]
    public void EveryUploadCallSite_TakesItsKeyFromStorageKeyHelper()
    {
        var repoRoot = RepoRoot.Locate();
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in EnumerateSourceFiles(repoRoot))
        {
            var text = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            foreach (Match match in CallSite.Matches(text))
            {
                var argument = FirstArgument(text, match.Index + match.Length);
                inspected++;

                if (IsGeneratedHere(argument, text))
                    continue;
                if (ReuseOfAStoredKey.Contains((fileName, argument)))
                    continue;

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetRelativePath(repoRoot, file)}:{line}  UploadAsync({argument}, …)");
            }
        }

        // 扫描面没塌：今天有 10 处调用点（直传 / MD5 去重路径 / MD5 复用重传 / 复制回退 / 压缩 / 解压 /
        // 缩略图 / 分片 / 分片完成 / 建版本）。数字变小说明扫描面丢了目录，不是代码变干净了。
        Assert.True(inspected >= 10, $"expected to inspect at least 10 UploadAsync call sites, found {inspected}");

        Assert.True(
            offenders.Count == 0,
            "Every storage key handed to IFileStorage.UploadAsync must come from StorageKeyHelper "
            + "(never from a caller-supplied name - see StorageKeyHelper's remarks and StorageKeyInvariantTests). "
            + "Offending call sites:\n  " + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> EnumerateSourceFiles(string repoRoot)
    {
        foreach (var root in ModuleRoots)
        {
            var directory = Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(directory), $"scan root missing: {root}");

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                // provider 实现里的 UploadAsync 是被调用的一方，不是调用点；bin/obj 是产物。
                if (relative.StartsWith("Providers/", StringComparison.Ordinal)
                    || relative.StartsWith("bin/", StringComparison.Ordinal)
                    || relative.StartsWith("obj/", StringComparison.Ordinal))
                    continue;

                yield return file;
            }
        }
    }

    /// <summary>键必须直接来自工厂，或来自一个由工厂赋值的局部变量。</summary>
    private static bool IsGeneratedHere(string argument, string fileText)
    {
        if (argument.Contains("StorageKeyHelper.", StringComparison.Ordinal))
            return true;

        if (!Regex.IsMatch(argument, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return false;

        return Regex.IsMatch(fileText, $@"\b{Regex.Escape(argument)}\s*=\s*StorageKeyHelper\.");
    }

    /// <summary>取第一个实参的文本（到深度 0 的第一个逗号为止）。</summary>
    private static string FirstArgument(string text, int start)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0) return text[start..i].Trim();
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                return text[start..i].Trim();
            }
        }

        return text[start..].Trim();
    }
}
