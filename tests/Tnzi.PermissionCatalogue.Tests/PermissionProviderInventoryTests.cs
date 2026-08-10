using System.Text.RegularExpressions;

namespace Tnzi.PermissionCatalogue.Tests;

/// <summary>
/// 门禁的门禁：<c>src</c> 下每一个权限 provider 都必须出现在
/// <see cref="PermissionCataloguePactTests.AllProviders"/> 里。
/// </summary>
/// <remarks>
/// <para>
/// 本项目原有两份清单（pact 的 <c>AllProviders</c> 与注册测试的 <c>ModulesWithCatalogues</c>）
/// 互相对账，删掉任一边的一行都会立刻现形。但两份<b>都是手工维护</b>的，于是有一个共同盲区：
/// 新模块的 provider 如果<b>两边都没登记</b>，两份清单彼此仍然一致，全部绿。
/// 后果是那个模块的码既不计入总数锁（293 不动），也没人检查它的模块是否真的注册了它；
/// 更糟的是「每个码只能有一个声明模块」这条保证也只覆盖列出来的 provider ——
/// 新 provider 复用了一个既有码串，撞码会被静默接受。
/// </para>
/// <para>
/// <b>刻意扫源码而不是扫程序集</b>：本测试项目只引用了 28 个模块，新模块在加进 csproj
/// 之前程序集根本不在进程里，扫描扫不到，于是「漏登记」表现为「没有漏登记」。
/// 这与 <c>ModuleInventoryTests</c> 采用源码扫描的理由是同一条。
/// </para>
/// </remarks>
public class PermissionProviderInventoryTests
{
    private static readonly Regex ProviderClass = new(
        @"class\s+(?<name>\w+)\s*:\s*(?<bases>[^{]*)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// 刻意不进 pact 的 provider。
    /// </summary>
    /// <remarks>
    /// <c>SettingsPermissionDefinitionProvider</c> 不声明静态码，而是在 seed 收集阶段从运行时
    /// 发现的 <c>SettingDefinitionGroup</c> <b>动态派生</b>每组一对 <c>.view</c>/<c>.update</c>。
    /// 它产出的码数随已加载模块变化，锁进总数断言会让「多加载一个模块」表现为门禁失败。
    /// pact 只锁静态目录，这条豁免是设计的一部分而不是遗漏。
    /// </remarks>
    private static readonly HashSet<string> IntentionallyOutsideThePact = new(StringComparer.Ordinal)
    {
        "SettingsPermissionDefinitionProvider",
    };

    [Fact]
    public void EveryProviderInSource_IsDeclaredInThePact()
    {
        var repoRoot = RepoRoot.Locate();
        var declaredInSource = ScanProviderClassNames(Path.Combine(repoRoot, "src"));

        // 下界而不是 ShouldNotBeEmpty：正则一旦退化成匹配不到东西，
        // 「扫到的都在 pact 里」照样成立，整条门禁会安静失效。
        declaredInSource.Count.ShouldBeGreaterThanOrEqualTo(25,
            $"源码只扫到 {declaredInSource.Count} 个权限 provider，远少于预期 —— 是扫描/正则坏了，不是 provider 真的变少了");

        var inPact = PermissionCataloguePactTests.AllProviders.Values
            .Select(p => p.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);

        var unlisted = declaredInSource
            .Where(name => !inPact.Contains(name) && !IntentionallyOutsideThePact.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        unlisted.ShouldBeEmpty(
            $"{unlisted.Count} 个权限 provider 存在于 src 但没进 PermissionCataloguePactTests.AllProviders："
            + $"{string.Join(", ", unlisted)}。它们的码不计入总数锁、不受「每个码只能有一个声明模块」约束，"
            + "也没有任何测试检查其模块是否真的注册了它 —— 撞码与漏注册都会被静默接受。");
    }

    /// <summary>扫描 <c>src</c> 下所有实现 <c>IPermissionDefinitionProvider</c> 的具体类名。</summary>
    private static HashSet<string> ScanProviderClassNames(string srcDir)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(srcDir))
            return found;

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(file);
            if (!text.Contains("IPermissionDefinitionProvider", StringComparison.Ordinal))
                continue;

            foreach (Match m in ProviderClass.Matches(StripLineComments(text)))
            {
                if (m.Groups["bases"].Value.Contains("IPermissionDefinitionProvider", StringComparison.Ordinal))
                    found.Add(m.Groups["name"].Value);
            }
        }

        return found;
    }

    /// <summary>剥掉行注释，免得注释里举例引用该接口的说明文字被当成真声明。</summary>
    private static string StripLineComments(string text)
        => string.Join('\n', text.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
