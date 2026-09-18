using System.Text.RegularExpressions;
using Match = System.Text.RegularExpressions.Match;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 源码门禁：存储模块里每一处把<b>调用方 / 上传者的字节</b>交给 provider 的调用，所在的方法都必须先跑过
/// <c>UploadGuard.RunAsync</c>（净化管线）。
/// </summary>
/// <remarks>
/// <para>
/// 行为测试守的是**今天已知的**五条写路径（直传 / MD5 直存 / 解压条目 / 分片完成 / 建版本）；这一条守的是
/// **明天新增的**：一条新写路径把字节交给 provider 而没过净化器，行为测试不会自动覆盖到它，而这里会红。
/// 2026-08-23 那次把闸门收进 <c>UploadGuard</c> 时写的是「三条」，解压与 MD5 直存两条就是这样漏掉的 ——
/// 解压只落了扩展名闸门，注册了病毒扫描器的部署以为拦住了，实际上 zip 里的每一条都绕过扫描器落进对象存储。
/// </para>
/// <para>
/// 判据与 <c>StorageKeyCallSiteGateTests</c> 同一路子：文本级、只约束框架自己的调用点。
/// 豁免的是那些字节本就来自**已经落库的记录**的路径（复制、打包、缩略图、按原键重传、分片临时块 ——
/// 分片在完成时合并后整体过一次），它们不是新的上传。
/// </para>
/// </remarks>
public class UploadSanitizationCallSiteGateTests
{
    private static readonly string[] ModuleRoots =
    [
        "src/Tnzi.Storage",
        "src/Tnzi.Storage.Workspace",
        "src/Tnzi.Storage.Cloud",
    ];

    /// <summary>
    /// 字节来自已落库记录（或在别处整体过闸）的调用点，按（文件，所在方法）豁免。
    /// </summary>
    private static readonly HashSet<(string File, string Method)> BytesAlreadyStored =
    [
        ("FileStorageService.cs", "CopyAsync"),                    // 复制一条现有记录的字节
        ("FileStorageService.cs", "CompressAsync"),                // 打包若干条现有记录
        ("FileStorageService.cs", "TryGetExistingFileByMd5Async"), // 记录在而对象丢了：按原键重传（调用方已过闸）
        ("FileThumbnailGenerator.cs", "GenerateAsync"),            // 缩略图：由已落库的原件（位图 / PDF 首页）派生
        ("FileChunkUploadService.cs", "UploadChunkAsync"),         // 临时分块；合并后在 CompleteChunkedUploadAsync 整体过闸
    ];

    private static readonly Regex CallSite = new(@"\.UploadAsync\(", RegexOptions.Compiled);
    private static readonly Regex MemberHeader = new(
        @"^\s{4}(?:public|private|protected|internal)\b[^;{=]*?\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex GuardRun = new(@"\b_guard\.RunAsync\(", RegexOptions.Compiled);

    [Fact]
    public void EveryUploadCallSite_RunsTheSanitizerPipelineFirst_OrIsAnExemptedReuse()
    {
        var repoRoot = RepoRoot.Locate();
        var offenders = new List<string>();
        var inspected = 0;
        var guarded = 0;

        foreach (var file in EnumerateSourceFiles(repoRoot))
        {
            var text = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);
            var headers = MemberHeader.Matches(text);

            foreach (Match call in CallSite.Matches(text))
            {
                inspected++;
                var header = headers.LastOrDefault(h => h.Index < call.Index);
                var method = header?.Groups["name"].Value ?? "<unknown>";
                var methodStart = header?.Index ?? 0;

                if (GuardRun.Match(text, methodStart, call.Index - methodStart).Success)
                {
                    guarded++;
                    continue;
                }

                if (BytesAlreadyStored.Contains((fileName, method)))
                    continue;

                var line = text.Take(call.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetRelativePath(repoRoot, file)}:{line}  in {method}");
            }
        }

        // 扫描面没塌：今天有 10 处调用点，其中 5 处在过闸的方法里。数字变小说明扫描面丢了目录。
        Assert.True(inspected >= 10, $"expected to inspect at least 10 UploadAsync call sites, found {inspected}");
        Assert.True(guarded >= 5, $"expected at least 5 call sites guarded by _guard.RunAsync, found {guarded}");

        Assert.True(
            offenders.Count == 0,
            "Every IFileStorage.UploadAsync call site that stores caller-supplied bytes must run "
            + "UploadGuard.RunAsync earlier in the same method (see UploadGuard's remarks), or be listed in "
            + "BytesAlreadyStored with the reason the bytes were already gated. Offending call sites:\n  "
            + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> EnumerateSourceFiles(string repoRoot)
    {
        foreach (var root in ModuleRoots)
        {
            var directory = Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(directory), $"scan root missing: {root}");

            foreach (var file in RepoScan.EnumerateFilesIn(directory, "*.cs"))
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
}
