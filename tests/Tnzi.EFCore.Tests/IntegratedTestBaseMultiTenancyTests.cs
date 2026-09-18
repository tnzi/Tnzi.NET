using Tnzi.TestBase;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 发布出去的 <see cref="IntegratedTestBase{TDbContext}"/> 必须给消费方一个开多租户开关的口子。
/// </summary>
/// <remarks>
/// <para>
/// ★ 开关自 09-12 起只经 <c>DbContextOptions</c>（<c>UseTnziMultiTenancy</c>）到达消费方上下文，而这个基类此前
/// 用裸 <c>AddDbContext</c> 且只暴露 <c>ConfigureServices</c> —— 多租户消费方照文档写的集成测试全部跑在单租户模型上
/// （没有 <c>TenantId</c> 列、没有租户过滤器），刚修的那类缺陷在框架自己发的测试底座上抓不到。
/// </para>
/// <para>
/// 基类不引用 <c>Tnzi.EFCore</c>（只依赖核心），故口子是 <c>ConfigureDbContextOptions</c> 钩子而不是一个 bool：
/// 消费方在自己的测试里调 <c>options.UseTnziMultiTenancy(true)</c>，它们本就引用 <c>Tnzi.EFCore</c>。
/// </para>
/// </remarks>
public class IntegratedTestBaseMultiTenancyTests : IntegratedTestBase<MultiTenancyProbeDbContext>
{
    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
    {
        options.UseTnziMultiTenancy(true);
    }

    [Fact]
    public void Hook_TurnsTheMultiTenancySwitchOn()
    {
        Assert.True(DbContext.IsMultiTenancyEnabled);
        Assert.NotNull(DbContext.Model.FindEntityType(typeof(ProbeTenantRow))!.FindProperty(nameof(ProbeTenantRow.TenantId)));
    }
}

/// <summary>对照组：不覆写钩子时与既往逐字相同 —— 单租户。</summary>
public class IntegratedTestBaseDefaultTests : IntegratedTestBase<MultiTenancyProbeDbContext>
{
    [Fact]
    public void WithoutTheHook_ItStaysSingleTenant()
    {
        Assert.False(DbContext.IsMultiTenancyEnabled);
        Assert.Null(DbContext.Model.FindEntityType(typeof(ProbeTenantRow))!.FindProperty(nameof(ProbeTenantRow.TenantId)));
    }
}
