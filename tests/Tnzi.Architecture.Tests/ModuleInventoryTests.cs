using System.Text.RegularExpressions;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 门禁的门禁：<c>src</c> 下每一个模块类都必须真的出现在
/// <see cref="AllModulesStartupModule"/> 加载出来的模块图里。
/// </summary>
/// <remarks>
/// <para>
/// 没有这一条，本项目会慢慢退化回它要修的那个状态：新增一个模块、忘了加进清单，
/// 它就不受任何架构门禁约束，而所有测试照样全绿。这正是历史上
/// <c>ModuleDependencyArchitectureTests</c> 只审计 12/54 个模块却一直是绿的原因。
/// </para>
/// <para>
/// <b>刻意扫源码而不是扫已加载程序集。</b>用 <c>AppDomain.GetAssemblies()</c> 有个致命
/// 假阴性：一个新模块如果连 <c>csproj</c> 引用都还没加，它的程序集根本不在进程里，
/// 扫描扫不到，于是「漏加」表现为「没有漏加」。源码目录是唯一不会因为漏配置而变小的真值源。
/// </para>
/// <para>
/// <b>边界</b>：只扫项目根目录下的 <c>*Module.cs</c>，这是框架对业务/基础设施模块的既定约定。
/// 核心 <c>Tnzi</c> 自己的几个内部模块（<c>CoreServicesModule</c> / <c>CachingModule</c> /
/// <c>EventBusModule</c> …）放在 <c>Modules/</c> 等子目录下，由框架自动加载、不需要也不应该
/// 出现在启动模块的清单里，故不在扫描范围内。
/// </para>
/// </remarks>
public class ModuleInventoryTests
{
    private static readonly Regex ModuleClassPattern = new(
        @"\b(?<modifiers>(?:public|internal|sealed|abstract|partial|\s)*)class\s+(?<name>\w+Module)\s*:\s*Tnzi(?:Application|Infrastructure|Framework|Core|Custom)Module",
        RegexOptions.Compiled);

    private static readonly Regex DependsOnAttributePattern = new(
        @"\[(?:Optional)?DependsOn\((?<args>[^\]]*)\)\]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TypeofPattern = new(
        @"typeof\(\s*(?<type>[\w.]+)\s*\)",
        RegexOptions.Compiled);

    /// <summary>模块类型层级的抽象基类 —— 供继承，绝不可作为依赖目标。</summary>
    private static readonly HashSet<string> AbstractModuleBaseClasses = new(StringComparer.Ordinal)
    {
        "TnziModuleBase",
        "TnziCoreModule",
        "TnziInfrastructureModule",
        "TnziFrameworkModule",
        "TnziApplicationModule",
        "TnziCustomModule",
    };

    [Fact]
    public void EveryModuleInSource_IsPresentInTheLoadedGraph()
    {
        var repoRoot = RepoRoot.Locate();
        var declared = EnumerateConcreteModuleClasses(Path.Combine(repoRoot, "src"));

        // 下界而不是 ShouldNotBeEmpty：正则一旦退化成只匹配到一两个模块，
        // 「扫到的都在图里」照样成立，整条门禁会安静地失效 —— 这正是它要防的那种假绿。
        // 数字取当前 43 个具体模块类的保守下界（源码共 44 个模块类，HostingModule 是 abstract 不进图），新增模块只会让它更宽松，删模块删到 40 以下才需要调。
        declared.Count.ShouldBeGreaterThanOrEqualTo(40,
            $"源码只扫到 {declared.Count} 个模块类，远少于预期 —— 是扫描/正则坏了，不是模块真的变少了");

        var loaded = ArchitectureModuleGraph.Load()
            .Modules.Select(m => m.Type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(d => !loaded.Contains(d.ClassName))
            .OrderBy(d => d.ClassName, StringComparer.Ordinal)
            .ToList();

        if (missing.Count > 0)
        {
            var report = string.Join(Environment.NewLine,
                missing.Select(m => $"  - {m.ClassName} ({m.RelativePath})"));
            Assert.Fail(
                $"{missing.Count} module(s) exist in src but never enter the module graph - they are "
                + $"exempt from every architecture gate. Add them to AllModulesStartupModule's "
                + $"[DependsOn] list (and the csproj):{Environment.NewLine}{report}");
        }
    }

    /// <summary>
    /// 没有任何模块可以 <c>[DependsOn]</c> 模块类型层级的<b>抽象基类</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TnziCoreModule</c> / <c>TnziApplicationModule</c> 这些是给模块<b>继承</b>的分层基类，
    /// 不是可加载的模块。写进 <c>[DependsOn]</c> 的后果是 <c>ModuleLoader</c> 试图实例化它们并抛
    /// <c>Module X must have a parameterless constructor</c> —— 即**任何显式加载该模块的应用启动即失败**。
    /// </para>
    /// <para>
    /// <c>ImagingModule</c> 就这么写了很久（<c>[DependsOn(typeof(TnziCoreModule))]</c>）。它没被发现是因为
    /// 唯一的消费者用 <c>[OptionalDependsOn]</c> 引它，而可选依赖从不主动加载目标 —— 那个模块
    /// 从来没真正进过模块图。有了这一条，同类错误在提交时就以人话报出来，而不是等某个消费应用
    /// 启动时撞上一句晦涩的反射异常。
    /// </para>
    /// </remarks>
    [Fact]
    public void NoModule_DependsOnAnAbstractModuleBaseClass()
    {
        var repoRoot = RepoRoot.Locate();
        var violations = new List<string>();

        foreach (var projectDir in Directory.GetDirectories(Path.Combine(repoRoot, "src")))
        {
            foreach (var file in Directory.GetFiles(projectDir, "*Module.cs"))
            {
                var code = StripComments(File.ReadAllText(file));
                foreach (Match attr in DependsOnAttributePattern.Matches(code))
                {
                    foreach (Match t in TypeofPattern.Matches(attr.Groups["args"].Value))
                    {
                        var simpleName = t.Groups["type"].Value.Split('.')[^1];
                        if (AbstractModuleBaseClasses.Contains(simpleName))
                        {
                            var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                            violations.Add($"  - {rel}: [DependsOn(typeof({simpleName}))]");
                        }
                    }
                }
            }
        }

        if (violations.Count > 0)
        {
            Assert.Fail(
                $"{violations.Count} module(s) depend on an abstract module base class - ModuleLoader "
                + $"cannot instantiate those, so any app explicitly loading such a module fails at "
                + $"startup. Depend on a real module instead:{Environment.NewLine}"
                + string.Join(Environment.NewLine, violations));
        }
    }

    /// <summary>剥掉注释行，免得注释里引用这些基类名的说明文字被当成真声明。</summary>
    private static string StripComments(string text)
        => string.Join('\n', text.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static List<(string ClassName, string RelativePath)> EnumerateConcreteModuleClasses(string srcDir)
    {
        var found = new List<(string, string)>();
        if (!Directory.Exists(srcDir))
            return found;

        foreach (var projectDir in Directory.GetDirectories(srcDir))
        {
            // 只看项目根：模块类按约定放在这里。
            foreach (var file in Directory.GetFiles(projectDir, "*Module.cs"))
            {
                var text = File.ReadAllText(file);
                foreach (Match match in ModuleClassPattern.Matches(text))
                {
                    // 抽象模块（HostingModule）是给消费方继承的，本身不进图。
                    if (match.Groups["modifiers"].Value.Contains("abstract", StringComparison.Ordinal))
                        continue;

                    var rel = Path.GetRelativePath(Path.GetDirectoryName(srcDir)!, file).Replace('\\', '/');
                    found.Add((match.Groups["name"].Value, rel));
                }
            }
        }

        return found;
    }
}
