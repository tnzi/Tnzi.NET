using System.Text.RegularExpressions;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 源码门禁：每一个声明了 <c>[FileField]</c> 实体的框架模块，其写入服务里都必须出现
/// <c>IFileReadAccessProbe</c>（或在下面的名单里写明为什么不用）。
/// </summary>
/// <remarks>
/// <para>
/// 把一个文件 id 写进 <c>[FileField]</c> = 把那份文件<b>发布</b>给这条记录的可见者
/// （<c>Public = true</c> 时是发布给全世界）。探针契约在核心程序集，没有任何编译期约束逼着
/// 写入侧去问它：Chat（2026-09-04）、Finance / Finance.Banking / Signing（2026-09-12）各修过一次，
/// 而 Identity 的头像 —— 全仓最强的一处（匿名可读、自助端点、不要权限码）—— 同一批里漏掉了。
/// 这条门禁守的是<b>明天新增的</b>那个模块：加了 <c>[FileField]</c> 实体而写入侧一次都没问过探针，这里会红。
/// </para>
/// <para>
/// 判据刻意做成程序集级的文本判定：「这个字段的值会不会来自请求体」无法机械判定，而「这个模块
/// 有没有问过探针」可以。真阳性（Identity）与豁免（两个：值从不来自请求 / 引用行不授予任何读权限）
/// 在真实语料上跑过一遍，比例支持它当门禁而不是诊断。豁免必须带理由，理由不成立时改代码不改名单。
/// </para>
/// </remarks>
public class FileFieldWriteProbeGateTests
{
    /// <summary>
    /// 不需要探针的模块与理由。加一行之前先问：这个模块里有没有一条路径把请求体里的 id 写进 <c>[FileField]</c>？
    /// </summary>
    private static readonly Dictionary<string, string> ExemptModules = new()
    {
        // Tnzi.Notification 曾在这里豁免（「没有发送器按 FileId 取字节」）。2026-09-12 起派发经核心的
        // IFileContentReader 以系统身份按 FileId 读字节，于是创建入口必须问探针 —— 它现在真的问了，不再豁免。
        // Invoice 的 PDF 路径 / URL / 文件 id 都由发票渲染在服务端生成，从不来自请求体。
        ["Tnzi.Payment.Billing"] = "Invoice PDF file fields are generated server-side, never taken from a request",
    };

    private static readonly Regex FileFieldAttribute = new(@"\[FileField(\(|\])", RegexOptions.Compiled);

    /// <summary>注释里提到 <c>[FileField]</c> 的实体（AI 的 AgentArtifact 解释自己为什么**不**标）不算声明。</summary>
    private static bool DeclaresAFileField(string file)
        => File.ReadLines(file)
            .Select(l => l.TrimStart())
            .Any(l => !l.StartsWith("//", StringComparison.Ordinal) && FileFieldAttribute.IsMatch(l));

    [Fact]
    public void EveryModuleWithAFileFieldEntity_AsksTheReadAccessProbeBeforeWriting()
    {
        var repoRoot = RepoRoot.Locate();
        var modulesWithFileFields = RepoScan.EnumerateFiles("src", "*.cs")
            .Where(f => PathSegments(repoRoot, f) is [_, _, "Entities", ..])
            .Where(DeclaresAFileField)
            .Select(f => PathSegments(repoRoot, f)[1])
            .Distinct()
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        // 扫描面没塌：今天有 7 个模块的实体带 [FileField]（Chat / Finance / Finance.Banking / Identity /
        // Notification / Payment.Billing / Signing）。数字变小说明扫描面丢了目录，不是代码变干净了。
        Assert.True(modulesWithFileFields.Count >= 7,
            $"expected at least 7 modules declaring [FileField] entities, found {modulesWithFileFields.Count}: {string.Join(", ", modulesWithFileFields)}");

        var offenders = new List<string>();
        foreach (var module in modulesWithFileFields)
        {
            if (ExemptModules.ContainsKey(module))
                continue;

            var asksTheProbe = RepoScan.EnumerateFiles($"src/{module}/Services", "*.cs")
                .Any(f => File.ReadAllText(f).Contains("IFileReadAccessProbe", StringComparison.Ordinal));
            if (!asksTheProbe)
                offenders.Add(module);
        }

        Assert.True(
            offenders.Count == 0,
            "Writing a request-supplied file id into a [FileField] publishes that file to the record's readers "
            + "(to everyone for Public = true). Ask IFileReadAccessProbe in the write service before mapping the id, "
            + "or add the module to ExemptModules with a reason that survives review. Modules with [FileField] "
            + "entities and no probe call in Services/:\n  " + string.Join("\n  ", offenders));

        var staleExemptions = ExemptModules.Keys.Except(modulesWithFileFields).ToList();
        Assert.True(staleExemptions.Count == 0,
            "ExemptModules names modules that no longer declare a [FileField] entity: " + string.Join(", ", staleExemptions));
    }

    private static string[] PathSegments(string repoRoot, string file)
        => Path.GetRelativePath(repoRoot, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
