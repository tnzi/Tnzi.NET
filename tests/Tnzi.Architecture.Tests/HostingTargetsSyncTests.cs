using System.Text.RegularExpressions;
using Tnzi.Hosting;
using Tnzi.Modules;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// <c>HostingModule</c> 的 <c>[OptionalDependsOn]</c> 列表与
/// <c>src/Tnzi.Hosting/build/Tnzi.Hosting.targets</c> 的 <c>TnziModules</c> 规则必须一一对应。
/// </summary>
/// <remarks>
/// <para>
/// 这两份清单表达同一件事的两半：<c>[OptionalDependsOn]</c> 决定<b>模块图</b>里的顺序，
/// targets 决定 NuGet 消费方<b>编译闭包</b>里有没有那个包。少了后者，
/// 消费方写 <c>&lt;TnziModules&gt;Payment.Billing&lt;/TnziModules&gt;</c> 只会因为子串匹配
/// 命中 <c>Payment</c> 而拿到父包，子模块的类型在他们那边根本不存在。
/// </para>
/// <para>
/// ★ 这条约定此前只写在 targets 文件第 9 行的注释里，而注释拦不住任何人：
/// 它已经<b>漂移过三次</b>都没有任何测试变红 —— Signing 从 Documents 提升为一等模块后
/// 那条规则仍指向不存在的 <c>Tnzi.Documents.Signing</c> 包；<c>AI.Cli</c> 从来没被加进去；
/// 2026-08-29 一次拆出十一个子模块时又整批漏掉。
/// 三次的共同点是：解决方案照常编译、全量测试照常绿、以 <c>ProjectReference</c> 消费框架的
/// 参考应用照常跑 —— <b>只有走 NuGet 的消费方受影响，而仓库里没有那样的消费方</b>。
/// 所以这件事必须由反射比对来守，不能靠人记得。
/// </para>
/// </remarks>
public class HostingTargetsSyncTests
{
    /// <summary>从模块类型名反推 targets 里应当出现的令牌，例如 PaymentBillingModule -> Payment.Billing。</summary>
    private static string TokenFor(Type moduleType)
    {
        // 程序集名就是包名，去掉 "Tnzi." 前缀即为令牌（Tnzi.Payment.Billing -> Payment.Billing）。
        var assemblyName = moduleType.Assembly.GetName().Name ?? string.Empty;
        return assemblyName.StartsWith("Tnzi.", StringComparison.Ordinal)
            ? assemblyName["Tnzi.".Length..]
            : assemblyName;
    }

    private static string TargetsPath() =>
        Path.Combine(RepoRoot.Locate(), "src", "Tnzi.Hosting", "build", "Tnzi.Hosting.targets");

    private static HashSet<string> DeclaredPackages()
    {
        var text = File.ReadAllText(TargetsPath());
        return Regex.Matches(text, @"<PackageReference\s+Include=""(?<id>[^""]+)""")
            .Select(m => m.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<Type> OptionalModules() =>
        typeof(HostingModule)
            .GetCustomAttributes(typeof(OptionalDependsOnAttribute), inherit: false)
            .Cast<OptionalDependsOnAttribute>()
            .SelectMany(a => a.DependedModuleTypes)
            .ToList();

    /// <summary>每一个 <c>[OptionalDependsOn]</c> 的模块都必须能被某条 targets 规则引用到。</summary>
    [Fact]
    public void EveryOptionalModule_HasAPackageRuleInTheTargetsFile()
    {
        var declared = DeclaredPackages();

        var missing = OptionalModules()
            .Select(t => t.Assembly.GetName().Name!)
            .Distinct(StringComparer.Ordinal)
            .Where(packageId => !declared.Contains(packageId))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        missing.ShouldBeEmpty(
            "以下模块在 HostingModule 上声明了 [OptionalDependsOn]，但 src/Tnzi.Hosting/build/Tnzi.Hosting.targets "
            + "里没有对应的 PackageReference 规则。走 NuGet 的消费方因此拿不到它们，"
            + "而解决方案编译、全量测试与 ProjectReference 消费方都察觉不到：\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>反过来：targets 里不能引用一个已经不存在的包。</summary>
    [Fact]
    public void EveryPackageRule_PointsAtAnAssemblyThatExists()
    {
        var repoRoot = RepoRoot.Locate();

        var dangling = DeclaredPackages()
            .Where(packageId => !Directory.Exists(Path.Combine(repoRoot, "src", packageId)))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        dangling.ShouldBeEmpty(
            "src/Tnzi.Hosting/build/Tnzi.Hosting.targets 引用了 src/ 下不存在的包（多半是模块改名或提升后忘了改这里）：\n  "
            + string.Join("\n  ", dangling));
    }

    /// <summary>
    /// 令牌必须真的能匹配上：条件是子串匹配，所以令牌写错一个字并不会报错，只会静默不生效。
    /// </summary>
    [Fact]
    public void EveryOptionalModule_TokenActuallyMatchesItsOwnRule()
    {
        var text = File.ReadAllText(TargetsPath());

        var unmatched = new List<string>();
        foreach (var moduleType in OptionalModules().DistinctBy(t => t.Assembly.GetName().Name))
        {
            var token = TokenFor(moduleType);
            var packageId = moduleType.Assembly.GetName().Name!;

            // 找到引用该包的那一段，确认它的条件令牌是这个模块自己的令牌的子串 ——
            // 否则消费方写出正确的令牌也拿不到这个包。
            var rule = Regex.Match(
                text,
                @"<ItemGroup Condition=""[^""]*Contains\('(?<token>[^']+)'\)[^""]*"">(?:(?!</ItemGroup>).)*?<PackageReference\s+Include=""" + Regex.Escape(packageId) + @"""",
                RegexOptions.Singleline);

            if (!rule.Success || !token.Contains(rule.Groups["token"].Value, StringComparison.Ordinal))
            {
                unmatched.Add($"{packageId} (令牌 '{token}' 匹配不上规则条件 '{rule.Groups["token"].Value}')");
            }
        }

        unmatched.ShouldBeEmpty(
            "以下包的 targets 规则条件与模块自身的令牌对不上，消费方写对令牌也引用不到：\n  "
            + string.Join("\n  ", unmatched));
    }

    private static string PropsPath() =>
        Path.Combine(RepoRoot.Locate(), "src", "Tnzi.Hosting", "build", "Tnzi.Hosting.props");

    /// <summary>
    /// ★ 签入的 <c>Tnzi.Hosting.props</c> 里 <c>TnziVersion</c> 的默认值只能是令牌 <c>__TNZI_VERSION__</c>，
    /// 由 <c>Tnzi.Hosting.csproj</c> 在打包时替换成包版本。
    /// </summary>
    /// <remarks>
    /// 它曾硬编码 <c>0.1.2</c>（2026-03-08 写下，当时的版本号），此后半年没人动过：没设 <c>TnziVersion</c> 的
    /// NuGet 消费方（脚手架产物就是）拿到 <c>Tnzi.Hosting *</c> = 最新，而 <c>TnziModules</c> 展开的每个包
    /// 解析成「最低的 ≥ 0.1.2」—— 最新的核心配上半年前的模块程序集，restore 不报错、AssemblyVersion 钉在
    /// Major.Minor 也不报错，只是静默地跑旧代码。仓库里没有任何 NuGet 消费方，此前唯一的门禁只读 targets 不读 props。
    /// </remarks>
    [Fact]
    public void PropsDefault_IsATokenSubstitutedAtPackTime_NotAHardCodedVersion()
    {
        var text = File.ReadAllText(PropsPath());

        var defaultValue = Regex.Match(text, @"<TnziVersion Condition=""'\$\(TnziVersion\)' == ''"">(?<value>[^<]+)</TnziVersion>");
        defaultValue.Success.ShouldBeTrue("src/Tnzi.Hosting/build/Tnzi.Hosting.props 必须给 TnziVersion 一个仅在未设置时生效的默认值");
        defaultValue.Groups["value"].Value.ShouldBe("__TNZI_VERSION__",
            "TnziVersion 的默认值必须是打包时替换的令牌，不能是写死的版本号：写死的数字从不随 build/version.props 走");

        Regex.IsMatch(text, @"\d+\.\d+\.\d+").ShouldBeFalse("props 里不得出现任何硬编码的版本号字面量");
    }

    /// <summary>
    /// csproj 必须真的把令牌替换掉并把生成的文件打进 <c>build\</c>：门禁只守签入文件时，替换那一步漏了照样全绿。
    /// 打出来的包里到底是什么，由 <c>build/nuget-pack.ps1</c> 与发布工作流在打包后拆包核对。
    /// </summary>
    [Fact]
    public void HostingCsproj_GeneratesThePropsFromTheTemplateAtPackTime()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot.Locate(), "src", "Tnzi.Hosting", "Tnzi.Hosting.csproj"));

        csproj.ShouldContain("__TNZI_VERSION__", customMessage: "csproj 必须替换 props 模板里的令牌");
        csproj.ShouldContain("$(PackageVersion)", customMessage: "替换值必须是包版本，让它随 build/version.props 与 --version-suffix 走");
        Regex.IsMatch(csproj, @"<None\s+Include=""build\\Tnzi\.Hosting\.props""[^>]*Pack=""true""")
            .ShouldBeFalse("签入的模板文件不能原样打进包里，打进去的必须是替换后的那份");
    }
}
