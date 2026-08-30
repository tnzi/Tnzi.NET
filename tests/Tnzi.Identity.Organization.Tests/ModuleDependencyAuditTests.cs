using Microsoft.Extensions.Options;
using Tnzi.Modules.Diagnostics;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.Organization.Tests;

/// <summary>
/// 本模块自己的跨模块依赖审计：<c>[DependsOn]</c> 必须覆盖它真正用到的每一个别人的服务。
/// </summary>
/// <remarks>
/// 生态级的那道门禁在 <c>tests/Tnzi.Architecture.Tests</c>，它从一份手工维护的模块清单出发 ——
/// 新模块加进那份清单之前，它对本模块<b>一个字都审不到</b>，而所有测试照样全绿
/// （那正是那道门禁自己的历史教训）。这条本地审计填的就是这段空窗，
/// 顺带回答「要不要挂 <c>[SuppressDependencyAudit]</c>」：依赖方向是子 → 父、
/// 父模块对本模块零引用，所以不需要，也不该防御性地加一条。
/// </remarks>
public class ModuleDependencyAuditTests
{
    private static ModuleLoadResult Load() => ModuleTestHelper.LoadAndCollectServiceMap<IdentityOrganizationModule>(
        new Dictionary<string, string?>
        {
            // EFCoreModule 的自动发现要求一个能解析到的 DbContext 类型，否则它整个出局，
            // 连带 IRepository<,> 不进服务图 —— 审计对所有消费仓储的模块变成假阴性。
            ["Database:DbContexts:0:DbContextType"] = typeof(AuditProbeDbContext).AssemblyQualifiedName,
        });

    [Fact]
    public void EveryModule_ConfiguresWithoutThrowing()
    {
        var result = Load();

        result.Failures.ShouldBeEmpty(
            string.Join(Environment.NewLine, result.Failures.Select(f => $"  - {f}")));
    }

    [Fact]
    public void CrossModuleDependencies_AreDeclared()
    {
        var result = Load();

        // 非空洞守卫：本模块必须真的出现在服务图里，否则「没有违规」只是没审到。
        result.ServiceMap.ShouldContainKey(typeof(IdentityOrganizationModule));
        result.ServiceMap[typeof(IdentityOrganizationModule)].ShouldNotBeEmpty();

        var violations = ModuleDependencyAuditor.AuditAndReport(result.Modules, result.ServiceMap);

        violations.ShouldBeEmpty(
            string.Join(Environment.NewLine, violations.Select(v => $"  - {v.Message}")));
    }
}

/// <summary>只为让 <c>EFCoreModule</c> 走完整注册路径而存在的 DbContext（不会建模型）。</summary>
public class AuditProbeDbContext : TnziDbContext<AuditProbeDbContext>
{
    /// <inheritdoc />
    public AuditProbeDbContext(
        DbContextOptions<AuditProbeDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null,
        TimeProvider? timeProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, currentTenant, dataFilterManager, timeProvider, multiTenancyOptions)
    {
    }
}
