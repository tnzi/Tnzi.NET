using System.Reflection;
using System.Text.RegularExpressions;
using Tnzi.Settings;
using Tnzi.System.Settings;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 架构门禁：`@tnzi/*/icons` 发布的图标清单必须覆盖框架真正会渲染的每一个 Iconify 名字。
/// </summary>
/// <remarks>
/// <para>
/// <b>清单为什么存在。</b><c>TSvgIcon</c> 经 <c>@iconify/vue</c> 的 <c>&lt;Icon&gt;</c> 渲染，
/// 而 <c>&lt;Icon&gt;</c> 遇到本地没有的名字会<b>当场去 Iconify 公共 API 取</b>。要断网运行的
/// 消费应用因此必须自己 <c>addCollection</c> 把图标打进包里 —— 而在这份清单存在之前，
/// 它想知道「框架要哪些图标」只有一条路：**反向扫这个仓库**，前端源码扫一遍 quoted
/// <c>"prefix:name"</c>，再扫一遍 C# 的 <c>[RuntimeSettingGroup(Icon = …)]</c>（那半个从不
/// 出现在任何前端源码里）。外部扫描当天就漏了一整个包：<c>@tnzi/ui-ai</c> 的 104 个名字。
/// </para>
/// <para>
/// <b>为什么门禁在这里。</b>缺一个名字的症状是<b>一个空白方块</b> —— 不报错、没有失败的请求、
/// 日志里什么都没有。也就是说，一份悄悄落后的清单会把原问题原封不动地搬到上一层，
/// 而且更难发现。清单由 <c>src/Tnzi.UI/tools/icons/generate.mjs</c> 生成，本门禁**不复用它**：
/// 后端那半走 <see cref="RuntimeSettingMetadataExtractor"/> **反射真实特性**（生产路径本身，
/// 不是正则），前端那半用本文件自己的匹配器重扫一遍。只与自己的生成器对得上的检查什么都证明不了。
/// </para>
/// <para>
/// <b>方向是单向的（包含而非相等）。</b>只断言「源码引用到的名字，清单里都有」。清单里多出
/// 几个（例如只出现在文档示例里的名字）不是缺陷 —— 消费方至多多打包几个用不上的字形，
/// 而 <c>pnpm icons:check</c> 那侧本来就要求逐字相等。两侧口径刻意不同：这一侧要独立，
/// 那一侧要精确。
/// </para>
/// </remarks>
public class IconManifestTests
{
    /// <summary>发布图标清单的包，与 <c>tools/icons/icon-manifest.mjs</c> 的 <c>ICON_PACKAGES</c> 同序。</summary>
    private static readonly string[] ManifestPackages = ["core", "ui", "ui-ai", "ui-admin"];

    /// <summary>累积清单所在的包：它的清单是全集（含后端那半）。</summary>
    private const string AggregatePackage = "ui-admin";

    private static readonly string[] SourceExtensions = [".ts", ".tsx", ".vue", ".mts", ".js", ".mjs"];

    /// <summary>生成的清单文件在包内的相对位置。</summary>
    private static readonly string ManifestRelativePath = Path.Combine("icons", "index.ts");

    /// <summary>清单文件里的一条名字。生成器一行一个、两空格缩进、单引号，故形态是确定的。</summary>
    private static readonly Regex ManifestEntry = new(
        @"^\s{2}'([a-z0-9-]+:[a-z0-9-]+)',$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// 源码里 <c>icon</c>-ish 位置上的一个 <c>prefix:name</c> 字面量。
    /// </summary>
    /// <remarks>
    /// 用来发现「清单收录的集合列表本身落后了」——一个全新的 Iconify 集合会让它的<b>每一个</b>
    /// 名字都缺席，而按前缀过滤的扫描对此天生是瞎的。必须是<b>闭合</b>的引号字面量：否则
    /// CSS 选择器 <c>.t-button-icon:active:not(:disabled)</c> 会被读成一个叫 <c>active:not</c> 的图标。
    /// </remarks>
    private static readonly Regex IconContext = new(
        @"\bicon[A-Za-z]*\s*[:=]\s*(['""])([a-z][a-z0-9-]*:[a-z0-9][a-z0-9-]*)\1",
        RegexOptions.Compiled);

    /// <summary>
    /// 绑定在 icon-ish 名字上的数组字面量的开头。
    /// </summary>
    /// <remarks>
    /// ★ 变异验证补出来的第二种形态：把 <c>'tabler:brand-github'</c> 丢进 <c>TIconPicker</c> 的
    /// <c>DEFAULT_ICONS</c>，两侧门禁**都是绿的**。<see cref="IconContext"/> 只认
    /// <c>icon…: 'a:b'</c>，而数组元素不是那个形状 —— 偏偏 <c>DEFAULT_ICONS</c> 正是管理员
    /// 挑图标的那张清单，选中值存进数据库、之后由 <c>TSvgIcon</c> 渲染。
    /// 树里最该被覆盖的一份名单，恰好落在盲区里。
    /// </remarks>
    private static readonly Regex IconArrayHead = new(
        @"\b([A-Za-z_$][\w$]*)\s*(?::[^=;\n]*)?=\s*\[",
        RegexOptions.Compiled);

    /// <summary>名字里带 icon/icons 的标识符。</summary>
    private static readonly Regex IconishName = new("icons?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>任意带引号的 <c>prefix:name</c>，只在已确认是图标数组的区域内使用。</summary>
    private static readonly Regex AnyIconLiteral = new(
        @"(['""])([a-z][a-z0-9-]*:[a-z0-9][a-z0-9-]*)\1",
        RegexOptions.Compiled);

    /// <summary>
    /// 配置中心发布的每一个分组图标，都必须在累积清单里。
    /// </summary>
    /// <remarks>
    /// 这些名字经 <c>SettingsCenterGroupDto.icon</c> 到达浏览器，由 ui-admin 的设置中心渲染，
    /// 而它们在前端源码里<b>一个字都不出现</b>。分组按运行时
    /// <c>AttributeSettingDefinitionProvider</c> 的口径重建（扫模块程序集 + 它自己的
    /// <c>MergeByGroupKey</c>），与 <see cref="SettingsPermissionGroupResolutionTests"/> 同一条路径 ——
    /// 门禁验的必须是生产那条路径。
    /// </remarks>
    [Fact]
    public void Every_settings_group_icon_is_published_in_the_icon_manifest()
    {
        var published = ManifestNames(AggregatePackage);
        var icons = SettingsGroupIcons();

        icons.ShouldNotBeEmpty(
            "No [RuntimeSettingGroup] icons were discovered across the module graph - the gate would pass vacuously.");

        var missing = icons.Where(icon => !published.Contains(icon)).Order(StringComparer.Ordinal).ToList();

        missing.ShouldBeEmpty(
            $"{missing.Count} settings-group icon(s) are missing from @tnzi/{AggregatePackage}/icons. "
            + "They reach the browser as SettingsCenterGroupDto.icon and appear in no frontend source, so an "
            + "application that bundles the published manifest renders a blank square for each - no error, no "
            + $"failed request. Run `pnpm -C src/Tnzi.UI icons:generate`.{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing.Select(m => $"  - {m}")));
    }

    /// <summary>
    /// 前端源码引用到的每一个图标，都必须在它自己那个包的清单里，也必须在累积清单里。
    /// </summary>
    /// <remarks>
    /// 两条一起断言是有意的：前者守「这个包的清单是不是自己的真相」，后者守「累积合并有没有
    /// 少并一个包」—— 后一种漏法在单看每个包时完全正常。
    /// </remarks>
    [Fact]
    public void Every_icon_referenced_in_frontend_source_is_published_in_the_icon_manifest()
    {
        var prefixes = KnownPrefixes();
        var aggregate = ManifestNames(AggregatePackage);
        var violations = new List<string>();

        foreach (var package in ManifestPackages)
        {
            var referenced = ScanPackageIconReferences(package, prefixes);
            referenced.ShouldNotBeEmpty(
                $"Scanned @tnzi/{package} and found no icon reference at all. Every manifest package renders "
                + "icons, so an empty scan means the file walk or the matcher is broken, not that the package "
                + "stopped using icons.");

            var own = ManifestNames(package);
            violations.AddRange(referenced
                .Where(icon => !own.Contains(icon))
                .Select(icon => $"  - {icon}  (referenced by @tnzi/{package}, missing from its own manifest)"));
            violations.AddRange(referenced
                .Where(icon => !aggregate.Contains(icon))
                .Select(icon => $"  - {icon}  (referenced by @tnzi/{package}, missing from @tnzi/{AggregatePackage})"));
        }

        violations.Sort(StringComparer.Ordinal);
        violations.ShouldBeEmpty(
            $"{violations.Count} icon reference(s) are not published in the generated manifest. An application "
            + "that bundles it renders a blank square for each of them. Run "
            + $"`pnpm -C src/Tnzi.UI icons:generate`.{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// 不得引用清单未收录的 Iconify 集合。
    /// </summary>
    /// <remarks>
    /// 生成器按<b>集合前缀</b>过滤候选（源码里满是 <c>update:modelValue</c> 这类同形串），
    /// 于是一个新集合会被整体跳过、一声不响。这条改按<b>调用位置</b>找，正是为了补上那个盲区。
    /// <para>
    /// 两种形态都要认：一处调用点（<see cref="IconContext"/>）与一份写死的名单
    /// （<see cref="IconArrayHead"/>）。只认前者时，往 <c>DEFAULT_ICONS</c> 里塞一个新集合的图标
    /// 两侧门禁全绿 —— 这是变异验证实测出来的，不是推想。
    /// </para>
    /// </remarks>
    [Fact]
    public void No_frontend_icon_reference_uses_a_collection_the_manifest_does_not_cover()
    {
        var prefixes = KnownPrefixes();
        var strays = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var package in ManifestPackages)
        {
            foreach (var file in PackageSourceFiles(package))
            {
                var text = File.ReadAllText(file);
                var found = IconContext.Matches(text).Select(match => match.Groups[2].Value)
                    .Concat(IconArrayRegions(text)
                        .SelectMany(region => AnyIconLiteral.Matches(region).Select(m => m.Groups[2].Value)));

                foreach (var name in found)
                {
                    if (prefixes.Contains(name[..name.IndexOf(':')])) continue;
                    strays.Add($"  - {name}  ({RepoRelative(file)})");
                }
            }
        }

        strays.ShouldBeEmpty(
            $"{strays.Count} icon reference(s) use an Iconify collection the manifest does not cover, so every "
            + "name in that collection is missing from it. Add the collection to ICON_PREFIXES in "
            + $"src/Tnzi.UI/tools/icons/icon-manifest.mjs and regenerate.{Environment.NewLine}"
            + string.Join(Environment.NewLine, strays));
    }

    /// <summary>
    /// 任何一个渲染图标的前端包，都必须自己发布一份清单。
    /// </summary>
    /// <remarks>
    /// 上面两条都从 <see cref="ManifestPackages"/> 这张写死的名单出发，所以一个**新增**的包
    /// 会同时逃过它们 —— 而症状仍旧是那个不声不响的空白方块。这条反过来从 <c>packages/</c>
    /// 的实际内容出发：要么没有一个图标引用（<c>@tnzi/mobile</c> 走 Vant，就是这种），
    /// 要么名单里必须有它。
    /// </remarks>
    [Fact]
    public void Every_frontend_package_that_renders_icons_publishes_a_manifest()
    {
        var packagesRoot = Path.Combine(RepoRoot.Locate(), "src", "Tnzi.UI", "packages");
        var prefixes = KnownPrefixes();
        var unlisted = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var directory in Directory.EnumerateDirectories(packagesRoot))
        {
            var package = Path.GetFileName(directory);
            if (ManifestPackages.Contains(package, StringComparer.Ordinal)) continue;
            if (!Directory.Exists(Path.Combine(directory, "src"))) continue;

            var referenced = ScanPackageIconReferences(package, prefixes);
            if (referenced.Count > 0)
            {
                unlisted.Add($"  - @tnzi/{package} ({referenced.Count} icon reference(s))");
            }
        }

        unlisted.ShouldBeEmpty(
            $"{unlisted.Count} frontend package(s) render icons but publish no icon manifest, so nothing in "
            + "this gate or in `pnpm icons:check` covers them. Add them to ICON_PACKAGES in "
            + "src/Tnzi.UI/tools/icons/icon-manifest.mjs (and to ManifestPackages here), wire the ./icons "
            + $"subpath export, and regenerate.{Environment.NewLine}"
            + string.Join(Environment.NewLine, unlisted));
    }

    /// <summary>按运行时口径重建配置组，取出非空的 <c>Icon</c>。</summary>
    private static IReadOnlyCollection<string> SettingsGroupIcons()
    {
        var graph = ArchitectureModuleGraph.Load();

        if (graph.Failures.Count > 0)
        {
            Assert.Fail(
                $"{graph.Failures.Count} module(s) failed to configure - their settings groups would silently "
                + $"drop out of this gate:{Environment.NewLine}"
                + string.Join(Environment.NewLine, graph.Failures.Select(f => $"  - {f}")));
        }

        var raw = graph.Modules
            .Select(module => module.Type.Assembly)
            .Distinct()
            .SelectMany(GetTypesSafe)
            .Select(RuntimeSettingMetadataExtractor.Extract)
            .Where(group => group != null)
            .Select(group => group!)
            .ToList();

        return AttributeSettingDefinitionProvider.MergeByGroupKey(raw)
            .Select(group => group.Icon)
            .Where(icon => !string.IsNullOrWhiteSpace(icon))
            .Select(icon => icon!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>某个包的生成清单里的全部名字。</summary>
    private static HashSet<string> ManifestNames(string package)
    {
        var relative = $"src/Tnzi.UI/packages/{package}/src/"
            + ManifestRelativePath.Replace(Path.DirectorySeparatorChar, '/');
        var text = RepoRoot.ReadText(relative);

        var names = ManifestEntry.Matches(text)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        names.ShouldNotBeEmpty(
            $"{relative} parsed to zero icon names. Either the file is not generated or the generator changed "
            + "its output shape, and every assertion in this gate would pass vacuously.");
        return names;
    }

    /// <summary>累积清单里出现过的集合前缀。</summary>
    private static HashSet<string> KnownPrefixes()
        => ManifestNames(AggregatePackage)
            .Select(name => name[..name.IndexOf(':')])
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>某个包的源码引用到的图标名。</summary>
    private static IReadOnlyCollection<string> ScanPackageIconReferences(string package, HashSet<string> prefixes)
    {
        // 前缀列表来自清单本身，所以这里的匹配器只需要认「引号包住的 a:b」再按前缀过滤。
        // ★ 它**认不出注释**：文档示例里写一个真实的 `'mdi:xxx'` 会被算成「框架渲染了它」。
        //   这是刻意不修的 —— 想跳过注释就得剥离 JS 注释，而按行剥离会把
        //   `https://` 之后的内容一起吃掉，那是**漏检**。对这道门禁而言误报的代价
        //   （改一行示例）远小于漏检（应用打包后渲染出空白方块）。
        //   在注释里举例图标时，用一个不在清单前缀里的名字，如 `'your-icons:foo'`。
        var literal = new Regex(@"(['""])([a-z0-9-]+:[a-z0-9][a-z0-9-]*)\1", RegexOptions.Compiled);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in PackageSourceFiles(package))
        {
            foreach (Match match in literal.Matches(File.ReadAllText(file)))
            {
                var name = match.Groups[2].Value;
                if (prefixes.Contains(name[..name.IndexOf(':')])) names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// 某个包 <c>src/</c> 下的源码文件，<b>不含它自己的生成清单</b>。
    /// </summary>
    /// <remarks>
    /// 排除清单不是整洁问题：那是一个装满图标名字的文件，就摆在被扫的树里。把它算进来，
    /// 「源码引用到的」会自动等于「清单里写的」，这条门禁就退化成一句同义反复。
    /// </remarks>
    private static IEnumerable<string> PackageSourceFiles(string package)
    {
        var root = Path.Combine(RepoRoot.Locate(), "src", "Tnzi.UI", "packages", package, "src");
        Directory.Exists(root).ShouldBeTrue($"Expected the icon-manifest package @tnzi/{package} at {root}.");

        var manifest = Path.Combine(root, ManifestRelativePath);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => SourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !string.Equals(file, manifest, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <paramref name="text"/> 中绑定在 icon-ish 名字上的数组字面量区域。
    /// </summary>
    /// <remarks>
    /// 数括号而不是写正则：图标数组跨很多行，且引号内的括号要跳过 —— 否则注释里一个落单的
    /// <c>']'</c> 会把区域提前截断，安静地把名单的后半截藏起来。
    /// </remarks>
    private static IEnumerable<string> IconArrayRegions(string text)
    {
        foreach (Match head in IconArrayHead.Matches(text))
        {
            if (!IconishName.IsMatch(head.Groups[1].Value)) continue;

            var open = head.Index + head.Length - 1;
            var depth = 0;
            char? quote = null;

            for (var i = open; i < text.Length; i++)
            {
                var ch = text[i];
                if (quote is not null)
                {
                    if (ch == '\\') i++;
                    else if (ch == quote) quote = null;
                    continue;
                }

                if (ch is '\'' or '"' or '`') quote = ch;
                else if (ch == '[') depth++;
                else if (ch == ']' && --depth == 0)
                {
                    yield return text[open..(i + 1)];
                    break;
                }
            }
        }
    }

    private static string RepoRelative(string file)
        => Path.GetRelativePath(RepoRoot.Locate(), file).Replace('\\', '/');

    private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null).Select(type => type!);
        }
    }
}
