using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// <c>docs/modules-index.json</c> 必须与源码一致。
/// </summary>
/// <remarks>
/// <para>
/// 这个文件是 <c>Tnzi.Mcp</c> 的模块元数据<b>权威来源</b> —— <c>DocIndex</c> 优先读它，
/// 读不到才回退文件系统扫描。它陈旧的直接后果是 MCP 给出错误答案：对账首次运行时
/// <c>AI</c> 模块的 <c>entities</c> 是空数组，而代码里有 22 个实体，于是「AI 模块有哪些实体」
/// 会被答成「没有」。
/// </para>
/// <para>
/// <b>为什么需要门禁而不是修一次就好</b>：该文件的消费方注释写着「由构建脚本预生成」，
/// 但仓库里<b>没有任何生成脚本</b>，它一直靠人手同步。没有门禁的话，下一个新增模块又会漂。
/// 首次运行时的实际缺口是 entities 缺 47 / controllers 缺 42，外加 3 个实体与 1 个控制器
/// 早已删除或随模块拆分搬走却没清理。
/// </para>
/// <para>
/// <b>刻意不覆盖 <c>services</c> 字段</b>：它是<b>人工策展</b>的「关键服务」而非全量清单 ——
/// <c>Audit</c> 索引里 4 个而源码有 6 个接口、<c>Template</c> 4 个而源码有 5 个，
/// 且 infrastructure/framework 类模块按惯例整个留空。把它纳入全量对账会产生大量假违规。
/// <c>description</c> 同理（人工撰写）。
/// </para>
/// </remarks>
public class ModulesIndexConsistencyTests
{
    private sealed record ModuleEntry
    {
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("assembly")] public string? Assembly { get; init; }
        [JsonPropertyName("entities")] public List<string> Entities { get; init; } = [];
        [JsonPropertyName("controllers")] public List<string> Controllers { get; init; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>src 下有 csproj 但不是 .NET 模块的目录。</summary>
    /// <remarks>
    /// <c>Tnzi.UI</c> 是 pnpm 前端工作区，那个 csproj 只用于把前端构建挂进 MSBuild，
    /// 它没有模块类也不该出现在模块索引里。
    /// </remarks>
    private static readonly HashSet<string> FrontendWorkspaces = new(StringComparer.Ordinal)
    {
        "Tnzi.UI",
    };

    /// <summary>
    /// <c>src</c> 下的每一个程序集都必须在索引里有条目，索引里的每一个条目也都必须指向真实目录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 另外两条测试只对账<b>索引里已有条目</b>的 entities / controllers，所以它们对
    /// 「整个模块根本没进索引」是盲的：新增 <c>src/Tnzi.Foo/</c> 并登记进
    /// <see cref="AllModulesStartupModule"/> 后 <c>ModuleInventoryTests</c> 就绿了，
    /// 而 <c>Tnzi.Mcp</c> 依旧不知道这个模块存在 —— <c>list_modules</c> 不列它、
    /// <c>get_module_info</c> 查不到，于是「框架里有没有 Foo 模块」被自信地答成「没有」。
    /// </para>
    /// <para>
    /// 反向同样要守：条目指向已删除的目录时，MCP 会继续报告一个不存在的模块。
    /// 这个失效形态实际发生过（线上 MCP 长期列着早已删除的 <c>HostingLite</c>）。
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryModuleAssembly_HasAnIndexEntry()
    {
        var repoRoot = RepoRoot.Locate();
        var indexed = LoadIndex(repoRoot)
            .Select(e => e.Assembly)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToHashSet(StringComparer.Ordinal)!;

        var srcDir = Path.Combine(repoRoot, "src");
        var onDisk = Directory.GetDirectories(srcDir)
            .Where(d => Directory.GetFiles(d, "*.csproj").Length > 0)
            .Select(Path.GetFileName)
            .Where(name => !FrontendWorkspaces.Contains(name!))
            .ToHashSet(StringComparer.Ordinal)!;

        // 下界：目录枚举一旦坏掉，「两边一致」会因为两边都是空集而成立。
        onDisk.Count.ShouldBeGreaterThan(40,
            $"只在 src 下扫到 {onDisk.Count} 个程序集，远少于预期 —— 是扫描坏了，不是模块真的变少了");

        var missing = onDisk.Except(indexed!).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var ghosts = indexed!.Except(onDisk!).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var problems = new List<string>();
        if (missing.Count > 0)
            problems.Add($"  src 里存在但索引没有条目（MCP 会答「模块不存在」）: {string.Join(", ", missing)}");
        if (ghosts.Count > 0)
            problems.Add($"  索引里有但 src 下没有该目录（MCP 会报告幽灵模块）: {string.Join(", ", ghosts)}");

        if (problems.Count > 0)
        {
            Assert.Fail(
                "docs/modules-index.json 的模块清单与 src 不一致。该文件是 Tnzi.Mcp 的模块元数据来源："
                + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }
    }

    [Fact]
    public void IndexedEntities_MatchTheSourceTree()
        => AssertFieldMatches("entities", e => e.Entities, SourceScanner.ScanEntities);

    [Fact]
    public void IndexedControllers_MatchTheSourceTree()
        => AssertFieldMatches("controllers", e => e.Controllers, SourceScanner.ScanControllers);

    private static void AssertFieldMatches(
        string fieldName,
        Func<ModuleEntry, List<string>> indexed,
        Func<string, string, SortedSet<string>> scan)
    {
        var repoRoot = RepoRoot.Locate();
        var entries = LoadIndex(repoRoot);
        entries.Count.ShouldBeGreaterThan(40,
            "模块索引条目异常地少，是读取/解析坏了而不是模块真的变少了");

        var problems = new List<string>();
        var comparedAssemblies = 0;

        foreach (var entry in entries)
        {
            // 只对账真实存在于 src/ 的程序集：核心 Tnzi 内部子模块（CoreServices 等）
            // 在索引里有条目但没有独立程序集目录，它们没有自己的 Entities//Controllers/。
            if (string.IsNullOrWhiteSpace(entry.Assembly)
                || !Directory.Exists(Path.Combine(repoRoot, "src", entry.Assembly)))
                continue;

            comparedAssemblies++;

            var fromIndex = new SortedSet<string>(indexed(entry), StringComparer.Ordinal);
            var fromSource = scan(repoRoot, entry.Assembly);

            var missing = fromSource.Except(fromIndex).ToList();
            var stale = fromIndex.Except(fromSource).ToList();

            if (missing.Count > 0)
                problems.Add($"  {entry.Name}: 索引缺少 {missing.Count} 项 -> {string.Join(", ", missing)}");
            if (stale.Count > 0)
                problems.Add($"  {entry.Name}: 索引里有源码中不存在的 {stale.Count} 项 -> {string.Join(", ", stale)}");
        }

        // 守住扫描本身：真被对账的程序集少得离谱时，「没有差异」是假绿而不是好消息。
        comparedAssemblies.ShouldBeGreaterThan(30,
            $"只对账了 {comparedAssemblies} 个程序集，远少于预期 —— 是索引的 assembly 字段或路径解析坏了");

        if (problems.Count > 0)
        {
            Assert.Fail(
                $"docs/modules-index.json 的 {fieldName} 与源码不一致（{problems.Count} 处）。"
                + $"该文件是 Tnzi.Mcp 的模块元数据来源，陈旧会让 MCP 给出错误答案："
                + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }
    }

    private static List<ModuleEntry> LoadIndex(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "docs", "modules-index.json");
        File.Exists(path).ShouldBeTrue($"找不到模块索引: {path}");
        return JsonSerializer.Deserialize<List<ModuleEntry>>(File.ReadAllText(path), JsonOptions)!;
    }
}
