using System.Reflection;
using System.Text.RegularExpressions;
using Tnzi.Security.Authorization;
using Tnzi.Settings;
using Tnzi.System.Settings;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 架构门禁：配置中心的每个 <see cref="SettingDefinitionGroup"/> 派生的一对权限码
/// （<c>{group}.settings.{slug}.view|update</c>）必须落在一个**真实被声明**的权限组下。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要门禁。</b><c>SettingsPermissionDefinitionProvider</c> 的归属组是
/// <c>SettingsPermissionNaming.GroupName</c> 从 <c>[RuntimeSettingGroup].Module</c>
/// （或显式 <c>PermissionGroup</c>）**猜**出来的，而 <c>Define</c> 那一刻它无从知道别的
/// provider 会不会声明这个组（provider 之间无顺序保证）。猜错的后果全落在
/// <c>PermissionDbSeeder</c>：它记一行
/// <c>"references module X but no such module was declared; skipping"</c>，然后把这两个码
/// <b>丢掉</b>。于是那一组配置在角色权限矩阵里连行都没有，只有超管靠 bypass 能看能改，
/// 而唯一的线索是启动日志里一行 warning。
/// </para>
/// <para>
/// 该 provider 的类注释长期声称「配置组存在 ⟹ 模块已加载 ⟹ 其权限组已声明」。这条不变式
/// <b>不成立</b>：模块可以有配置组而没有任何权限组（<c>Tnzi.Identity.Presence</c> 一个 admin
/// 控制器都没有），也可以把权限码声明在与模块名不同的组下（<c>Tnzi.AspNetCore</c> 的
/// <c>Module = "Web"</c> 归到 <c>system</c>）。2026-08-09 实测到 4 个被丢弃的码。
/// </para>
/// <para>
/// <b>为什么落在本项目。</b>判定要同时看「全部配置组」与「全部权限组声明」，只有
/// <see cref="AllModulesStartupModule"/> 这张全模块图能两样都给全。零 allowlist：
/// 新模块加进那张清单即自动纳入本门禁。
/// </para>
/// </remarks>
public class SettingsPermissionGroupResolutionTests
{
    /// <summary>权限码的单段：字母开头，此后只允许字母数字（camelCase 段）。</summary>
    private static readonly Regex CodeSegment = new("^[a-zA-Z][a-zA-Z0-9]*$", RegexOptions.Compiled);

    [Fact]
    public void Every_settings_group_parents_on_a_declared_permission_group()
    {
        var graph = ArchitectureModuleGraph.Load();
        AssertFixtureIsSound(graph);

        var declared = DeclaredPermissionGroups(graph);
        var settingsGroups = SettingsGroups(graph);

        var violations = settingsGroups
            .Where(g => !declared.Contains(SettingsPermissionNaming.GroupName(g)))
            .ToList();

        if (violations.Count > 0)
        {
            var report = string.Join(Environment.NewLine, violations.Select(g =>
                $"  - settings group '{g.Key}' (Module=\"{g.ModuleName}\", from {DescribeOwners(g)}) derives permission group "
                + $"'{SettingsPermissionNaming.GroupName(g)}', which no IPermissionDefinitionProvider declares. "
                + $"Codes {SettingsPermissionNaming.ViewCode(g)} / {SettingsPermissionNaming.UpdateCode(g)} would be dropped by PermissionDbSeeder."));
            Assert.Fail(
                $"{violations.Count} settings group(s) derive an undeclared permission group, so their view/update codes "
                + $"are silently skipped at seed time and can never be granted to a role:{Environment.NewLine}{report}"
                + $"{Environment.NewLine}Fix: set [RuntimeSettingGroup(PermissionGroup = \"<existing group>\")] on the Options "
                + $"type (e.g. \"identity\", \"system\"), or have the owning module declare that group in its "
                + $"IPermissionDefinitionProvider. Declared groups: {string.Join(", ", declared.Order())}.");
        }
    }

    /// <summary>
    /// 派生码的每一段必须是干净的 camelCase 标识符。
    /// </summary>
    /// <remarks>
    /// 与上一条正交：上一条问「组存在吗」，这条问「码长得像码吗」。缺 <c>[RuntimeSettingGroup]</c>
    /// 时元数据会拿配置节字符串顶替，<c>Notification:Dispatch</c> 于是同时污染组名和 slug
    /// （<c>notificationdispatch.settings.notification:dispatch.view</c>）—— 组名那半边上一条能抓，
    /// slug 里的冒号只有这条能抓。
    /// </remarks>
    [Fact]
    public void Every_derived_settings_code_is_a_clean_identifier()
    {
        var graph = ArchitectureModuleGraph.Load();
        AssertFixtureIsSound(graph);

        var violations = new List<string>();
        foreach (var group in SettingsGroups(graph))
        {
            var code = SettingsPermissionNaming.ViewCode(group);
            SettingsPermissionNaming.IsSettingsPermissionCode(code).ShouldBeTrue(
                $"'{code}' (settings group '{group.Key}') is not recognised as a settings permission code.");

            var bad = code.Split('.').Where(s => !CodeSegment.IsMatch(s)).ToList();
            if (bad.Count > 0)
            {
                violations.Add(
                    $"  - settings group '{group.Key}' (Module=\"{group.ModuleName}\", from {DescribeOwners(group)}) "
                    + $"derives '{code}' with non-identifier segment(s): {string.Join(", ", bad.Select(s => $"'{s}'"))}");
            }
        }

        if (violations.Count > 0)
        {
            Assert.Fail(
                $"{violations.Count} settings group(s) derive a malformed permission code:{Environment.NewLine}"
                + string.Join(Environment.NewLine, violations)
                + $"{Environment.NewLine}Usually the Options type is missing [RuntimeSettingGroup], so the raw config "
                + $"section (e.g. \"Notification:Dispatch\") is used as group metadata. Add the attribute with a kebab-case "
                + $"Key and the owning Module, or set PermissionSlug explicitly.");
        }
    }

    /// <summary>
    /// 夹具自检。模块配置失败会让该模块的 provider 注册缺席，「门禁没抓到违规」与
    /// 「门禁根本没看到那个模块」在断言层面长得一模一样，必须先把这两者分开。
    /// </summary>
    private static void AssertFixtureIsSound(ModuleLoadResult graph)
    {
        if (graph.Failures.Count > 0)
        {
            Assert.Fail(
                $"{graph.Failures.Count} module(s) failed to configure; their permission providers are missing from the "
                + $"service map, so this gate cannot tell 'no violation' from 'not scanned':{Environment.NewLine}"
                + string.Join(Environment.NewLine, graph.Failures.Select(f => $"  - {f}")));
        }
    }

    /// <summary>模块图里所有 <see cref="IPermissionDefinitionProvider"/> 声明过的权限组 name。</summary>
    private static HashSet<string> DeclaredPermissionGroups(ModuleLoadResult graph)
    {
        var context = new PermissionDefinitionContext();
        var providerTypes = graph.ServiceMap.Values
            .SelectMany(descriptors => descriptors)
            .Where(d => d.ServiceType == typeof(IPermissionDefinitionProvider))
            .Select(d => d.ImplementationType)
            .Where(t => t != null && t.GetConstructor(Type.EmptyTypes) != null)
            .Distinct()
            .ToList();

        // 至少要有一个可无参构造的 provider，否则下面的「零违规」毫无意义。
        providerTypes.ShouldNotBeEmpty(
            "No IPermissionDefinitionProvider registrations were found in the module graph - the gate would pass vacuously.");

        foreach (var type in providerTypes)
        {
            ((IPermissionDefinitionProvider)Activator.CreateInstance(type!)!).Define(context);
        }

        return context.Groups.Values
            .Select(g => g.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 按运行时 <c>AttributeSettingDefinitionProvider</c> 的口径重建配置组：逐模块程序集扫描
    /// <c>[RuntimeSetting]</c>，再经它自己的 <c>MergeByGroupKey</c> 合并（不是另写一份合并逻辑，
    /// 否则门禁验的就不是生产那条路径了）。
    /// </summary>
    private static IReadOnlyList<SettingDefinitionGroup> SettingsGroups(ModuleLoadResult graph)
    {
        var raw = graph.Modules
            .Select(m => m.Type.Assembly)
            .Distinct()
            .SelectMany(GetTypesSafe)
            .Select(RuntimeSettingMetadataExtractor.Extract)
            .Where(g => g != null)
            .Select(g => g!)
            .ToList();

        var merged = AttributeSettingDefinitionProvider.MergeByGroupKey(raw);
        merged.ShouldNotBeEmpty(
            "No [RuntimeSetting] groups were discovered across the module graph - the gate would pass vacuously.");
        return merged;
    }

    /// <summary>合并组的贡献者类型，用于报错时指出该改哪个 Options 类。</summary>
    private static string DescribeOwners(SettingDefinitionGroup group)
        => group.OptionsTypes is { Count: > 0 } types
            ? string.Join(", ", types.Select(t => t.FullName))
            : "(unknown Options type)";

    private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Select(t => t!);
        }
    }
}
