using System.Text.RegularExpressions;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 官网的模块目录与实际发布的包对账。
/// </summary>
/// <remarks>
/// <para>
/// <b>这条门禁为什么存在</b>：官网数字漂移在 2026-08-15 合仓时是第三次被发现 ——
/// 前两次分别记在 <c>website/CLAUDE.md</c> 的 Modification Notes 里（一次把
/// modules 33→32、tools 11→15、resources 9→19），这一次是漏列 10 个包、
/// 四个统计数字错了三个（36→45、12→20、7→8）。
/// </para>
/// <para>
/// <b>同类缺陷第三次出现，就不该再靠人记得。</b>官网与它的数据源合仓之前分居两地，
/// 对账要跨仓翻找；现在两者同仓，对账成本降到一次目录枚举，没有理由再让它漂。
/// </para>
/// <para>
/// 刻意<b>不</b>校验每张卡片的文案 —— 那是产品内容，不是可机械对账的事实。
/// 这里只守三件事：包有没有被漏列、有没有列了不存在的包、统计数字对不对。
/// </para>
/// <para>
/// ★ 2026-08-15 官网重构后，模块目录从 <c>ModulesPage.vue</c> 的硬编码数组
/// 迁到 <c>website/src/data/modules/</c>（每个包一个文件），页面改为数据驱动。
/// 本门禁跟着改扫那个目录。<b>页面上的实体/服务/控制器/依赖/表前缀现在直接读
/// <c>docs/modules-index.json</c></b>（构建期经 virtual module 注入），
/// 那部分从结构上不可能漂移，故不在本门禁的对账范围 —— 这里只管
/// 「有没有为每个包写展示条目」。
/// </para>
/// <para>
/// ⚠️ <b>本文件在公开镜像里跑不起来</b>：它读 <c>website/</c>（整个目录不投影）
/// 与 <c>tests/Tnzi.Mcp.Tests/</c>（在 <c>mirror.ps1</c> 的 <c>$ExcludePaths</c> 里）。
/// 同项目的 <c>ModulesIndexConsistencyTests</c> 已经有同类问题（读不投影的 <c>docs/</c>），
/// 那边的注释里承认了这一点。公开仓不跑任何质量流水线，所以这不会让 CI 变红，
/// 但**克隆下来自己跑 <c>dotnet test</c> 的人会看到失败**。
/// 要么把本文件与 Mcp 那条一起加进镜像的排除项，要么把 <c>website/</c> + <c>docs/</c>
/// 一并投影 —— 两条路都行，「让它在公开仓静默失败」不行。
/// </para>
/// </remarks>
public class WebsiteModuleCatalogTests
{
    private const string ModuleDataDir = "website/src/data/modules";
    private const string SiteSrcDir = "website/src";
    private const string McpGuidePath = "website/src/pages/docs/McpGuide.vue";

    /// <summary>
    /// 核心包<b>有</b>自己的详情页（<c>core.ts</c>），所以不再有豁免项。
    /// 保留这个集合是因为「某个包刻意不出卡片」是可能的正当状态，
    /// 真出现时应当在这里登记理由，而不是把它从对账里悄悄拿掉。
    /// </summary>
    private static readonly HashSet<string> IntentionallyWithoutCard =
        new(StringComparer.Ordinal);

    /// <summary>前端 pnpm 工作区，不是 NuGet 包（与 build/nuget-pack.ps1 的排除项一致）。</summary>
    private static readonly HashSet<string> NotANuGetPackage =
        new(StringComparer.Ordinal) { "Tnzi.UI" };

    [Fact]
    public void EveryPublishedPackage_AppearsInTheCatalog()
    {
        var repoRoot = RepoRoot.Locate();
        var real = RealPackages(repoRoot);
        var listed = ListedPackages(repoRoot);

        // 下界：目录枚举一旦坏掉，「两边一致」会因为两边都是空集而成立。
        real.Count.ShouldBeGreaterThan(40,
            $"只扫到 {real.Count} 个包，远少于预期 —— 是扫描坏了，不是包真的变少了");

        var missing = real
            .Where(p => !IntentionallyWithoutCard.Contains(p) && !listed.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        missing.ShouldBeEmpty(
            $"以下包在 src/ 下存在，但官网 {ModuleDataDir}/ 下没有对应条目。\n"
            + "新增模块时要在那里加一个 {slug}.ts 并挂进 index.ts 的 modules 数组 ——\n"
            + "漏掉的表现不是报错，是官网长期少展示了框架的一部分能力。\n"
            + "确实不该出卡片的，登记进 IntentionallyWithoutCard 并写明理由。");
    }

    [Fact]
    public void Catalog_HasNoGhostPackages()
    {
        var repoRoot = RepoRoot.Locate();
        var real = RealPackages(repoRoot);

        var ghosts = ListedPackages(repoRoot)
            .Where(p => !real.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        ghosts.ShouldBeEmpty(
            $"官网 {ModuleDataDir}/ 列出了 src/ 下不存在的包 —— 包被删除或改名后没同步官网。");
    }

    /// <summary>
    /// 每个模块条目都必须挂进 <c>index.ts</c> 的 <c>modules</c> 数组。
    /// </summary>
    /// <remarks>
    /// 写了 <c>{slug}.ts</c> 却忘了 import 进数组，是这套数据结构下唯一的静默失效方式：
    /// 文件在、类型对、编译过，页面上就是没有那张卡片。上面两条对账扫的是目录里的文件，
    /// 正好扫不出这一种。
    ///
    /// ★ <b>这条门禁的第一版是漏的</b>：直接在整份 index.ts 上跑正则，于是**被注释掉的**
    /// import 照样匹配，变异验证时把一行 import 注释掉，门禁全绿。注释掉一行远比删掉一行
    /// 常见（调试时随手为之，忘了还原），所以先剥掉行注释再匹配。
    /// </remarks>
    [Fact]
    public void EveryModuleFile_IsWiredIntoTheIndex()
    {
        var repoRoot = RepoRoot.Locate();
        var dir = Path.Combine(repoRoot, ModuleDataDir);
        var index = StripLineComments(File.ReadAllText(Path.Combine(dir, "index.ts")));

        var files = Directory.GetFiles(dir, "*.ts")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && name != "index")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        files.Count.ShouldBeGreaterThan(40,
            $"只扫到 {files.Count} 个模块数据文件 —— 是扫描坏了，不是模块真的变少了");

        var unwired = files
            .Where(name => !Regex.IsMatch(index, $@"from '\./{Regex.Escape(name!)}'"))
            .ToList();

        unwired.ShouldBeEmpty(
            $"以下文件在 {ModuleDataDir}/ 下，但没有被 index.ts import —— "
            + "页面上不会出现它们，且不会有任何报错。");
    }

    /// <summary>
    /// 上一条的自检：注释掉的 import 必须**不**算接线。
    /// </summary>
    /// <remarks>
    /// 这条锁住的是那个漏洞本身。没有它，将来有人「简化」<see cref="StripLineComments"/>
    /// 时会把同一个洞重新打开，而所有测试照样全绿。
    /// </remarks>
    [Fact]
    public void IndexWiringCheck_IgnoresCommentedOutImports()
    {
        const string source = """
            import { alpha } from './alpha'
            // import { beta } from './beta'
              //  import { gamma } from './gamma'
            """;

        var stripped = StripLineComments(source);

        Regex.IsMatch(stripped, @"from '\./alpha'").ShouldBeTrue("真实 import 必须仍被识别");
        Regex.IsMatch(stripped, @"from '\./beta'").ShouldBeFalse("注释掉的 import 不算接线");
        Regex.IsMatch(stripped, @"from '\./gamma'").ShouldBeFalse("缩进后的注释同样不算");
    }

    /// <summary>剥掉 <c>//</c> 行注释。字符串字面量里出现 <c>//</c> 的情况不在这份文件里发生。</summary>
    private static string StripLineComments(string source) =>
        string.Join(
            '\n',
            source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void HardCodedCounts_MatchReality()
    {
        var repoRoot = RepoRoot.Locate();
        var expected = RealPackages(repoRoot).Count;

        var wrong = ScanProseCounts(repoRoot).Where(p => p.Value != expected).ToList();

        wrong.ShouldBeEmpty(
            $"以下文案里的数字与实际的 {expected} 个包不符（数字改了但句子忘了改）。\n"
            + "★ 页面上的计数应当从 `virtual:site-facts` 取，不要手写。\n"
            + string.Join("\n", wrong.Select(w => $"  {w.Location}: \"{w.Text}\"")));
    }

    /// <summary>
    /// 上一条的<b>门禁的门禁</b>：证明那几个正则真的还能匹配。
    /// </summary>
    /// <remarks>
    /// 官网重构后页面上的计数改从 <c>virtual:site-facts</c> 插值，理想状态下
    /// <see cref="ScanProseCounts"/> 一处也扫不到 —— 那正是我们想要的。
    /// 但这也意味着<b>不能</b>再拿「扫到了 N 处」当正则有效的证据：
    /// 「正则坏了」与「没人手写数字」会给出同一个观测结果，而前者让门禁彻底失效。
    ///
    /// 所以下界改成对固定样本自检。样本逐字取自重构前真实出现过的三种写法。
    /// </remarks>
    [Fact]
    public void CountScanner_StillMatchesEveryKnownShape()
    {
        var samples = new[]
        {
            ("45 composable modules with lifecycle hooks", 45),
            ("45 modules, load what you need", 45),
            ("36 NuGet packages across core, framework", 36),
            ("{ value: '45', label: 'NuGet Packages' }", 45),
        };

        foreach (var (text, expected) in samples)
        {
            var hits = MatchCountPatterns(text).ToList();
            hits.ShouldNotBeEmpty(
                $"计数扫描的正则匹配不到已知写法 \"{text}\" —— 正则失效了，"
                + "HardCodedCounts_MatchReality 会静默放行一切");
            hits.ShouldAllBe(v => v == expected);
        }
    }

    /// <summary>
    /// 扫 <c>website/src</c> 下所有把包数写进文案的地方。
    /// </summary>
    /// <remarks>
    /// 这条是<b>补了两次</b>的：第一版只查结构化的统计块与首页数字，于是 2026-08-15
    /// 部署前才发现文案里还写着旧的 36；补上 <c>N modules</c> 之后，<c>/modules</c>
    /// 页 hero 里的「36 NuGet packages」依然全绿放行 —— 名词是 packages 不是 modules，
    /// 而分层卡片的 <c>count: 12</c> 连句子都不是。
    ///
    /// 教训是：<b>同一个事实在页面上有几种写法，门禁就得覆盖几种</b>。
    /// 现在覆盖三种形态；真正的修法是让页面根本不手写这个数（改从 site-facts 取），
    /// 这道门禁于是退化为「有没有人又开始手写」的守卫。
    /// </remarks>
    private static readonly string[] CountPatterns =
    [
        // "45 composable modules" / "45 modules, load what you need"
        @"\b(\d+)\s+(?:composable\s+)?modules\b",
        // "36 NuGet packages across core…"
        @"\b(\d+)\s+(?:NuGet|backend)\s+packages\b",
        // { value: '45', label: 'NuGet Packages' }
        @"value:\s*'(\d+)',\s*label:\s*'NuGet Packages'",
    ];

    private static IEnumerable<int> MatchCountPatterns(string text) =>
        CountPatterns.SelectMany(p => Regex.Matches(text, p).Select(m => int.Parse(m.Groups[1].Value)));

    private static List<(string Location, string Text, int Value)> ScanProseCounts(string repoRoot)
    {
        var srcDir = Path.Combine(repoRoot, SiteSrcDir);
        var found = new List<(string, string, int)>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".vue", StringComparison.Ordinal)
                              || f.EndsWith(".ts", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            var location = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            foreach (var pattern in CountPatterns)
            {
                foreach (Match m in Regex.Matches(text, pattern))
                    found.Add((location, m.Value, int.Parse(m.Groups[1].Value)));
            }
        }

        return found;
    }

    /// <summary>
    /// 官网展示的 MCP 暴露面数字，必须与服务端的断言一致。
    /// </summary>
    /// <remarks>
    /// 官网的 <c>McpSection</c> 上摆着「17 Tools / 21 Resources / 6 Prompts」。
    /// 这三个数在两个仓库合并之前是手抄的，抄错过一次：站点长期写着 15 / 19 / 4。
    ///
    /// 服务端那侧由 <c>tests/Tnzi.Mcp.Tests/McpSurfaceInventoryTests</c> 硬断言，
    /// 这里做的是把官网的数字与那份断言对上 —— 两处**都**改才过得去，
    /// 单改一处会红。刻意不去反射 Tnzi.Mcp 程序集：本项目不引用它，
    /// 而为了三个整数引入一整个 web 应用的程序集闭包不划算。
    /// </remarks>
    [Fact]
    public void McpSurfaceNumbers_MatchTheServerSideAssertion()
    {
        var repoRoot = RepoRoot.Locate();
        var inventory = RepoRoot.ReadText("tests/Tnzi.Mcp.Tests/McpSurfaceInventoryTests.cs");
        var section = RepoRoot.ReadText(McpGuidePath);

        var expected = new (string Attribute, string Label)[]
        {
            ("McpServerToolAttribute", "Tools"),
            ("McpServerResourceAttribute", "Resources"),
            ("McpServerPromptAttribute", "Prompts"),
        };

        foreach (var (attribute, label) in expected)
        {
            var server = Regex.Match(
                inventory,
                $@"CountMembersWith<{Regex.Escape(attribute)}>\(\)\.ShouldBe\((\d+)");
            server.Success.ShouldBeTrue(
                $"在 McpSurfaceInventoryTests 里找不到 {attribute} 的断言 —— "
                + "那边的写法变了，这条对账要跟着改，否则它会静默失效");

            var site = Regex.Match(section, $@"count:\s*(\d+),\s*label:\s*'{label}'");
            site.Success.ShouldBeTrue(
                $"在 {McpGuidePath} 里找不到 '{label}' 的计数 —— "
                + "页面结构变了，这条门禁需要跟着改");

            site.Groups[1].Value.ShouldBe(server.Groups[1].Value,
                $"官网展示的 MCP {label} 数与服务端断言不符。"
                + "改 MCP 暴露面时官网要跟着改 —— 站点上一个自信的错数字比没有数字更糟。");
        }
    }

    /// <summary>src/ 下会被 build/nuget-pack.ps1 打成 NuGet 包的项目。</summary>
    private static List<string> RealPackages(string repoRoot) =>
        Directory.GetDirectories(Path.Combine(repoRoot, "src"))
            .Where(d => Directory.GetFiles(d, "*.csproj").Length > 0)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !NotANuGetPackage.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList()!;

    /// <summary>官网模块数据里列出的 NuGet 包（<c>@tnzi/*</c> 是 npm 包，不在此列）。</summary>
    private static List<string> ListedPackages(string repoRoot)
    {
        var dir = Path.Combine(repoRoot, ModuleDataDir);

        return Directory.GetFiles(dir, "*.ts")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"pkg:\s*'([^']+)'")
                .Select(m => m.Groups[1].Value))
            .Where(p => !p.StartsWith("@tnzi/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }
}
