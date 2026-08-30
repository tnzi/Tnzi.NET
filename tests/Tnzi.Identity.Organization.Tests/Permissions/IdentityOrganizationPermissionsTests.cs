using Tnzi.Identity.Organization.Permissions;
using Tnzi.Identity.Permissions;
using Tnzi.Security.Authorization;

namespace Tnzi.Identity.Organization.Tests.Permissions;

/// <summary>
/// 四个 <c>organization.*</c> 码搬家不改名，父组仍是 <c>identity</c>。
/// </summary>
/// <remarks>
/// 码串是**持久化契约**：已授出去的角色行按码串匹配，管理端路由的 <c>meta.permission</c>
/// 也逐字引用它们。改一个字符不会报错，只会让某天起一批人打不开组织页，
/// 而权限矩阵里那一行看上去还在。
/// 生态级的总数锁与「每个码只能有一个声明模块」在 <c>tests/Tnzi.PermissionCatalogue.Tests</c>，
/// 这里守的是本包自己这一份。
/// </remarks>
public class IdentityOrganizationPermissionsTests
{
    private static PermissionDefinitionContext Define(IPermissionDefinitionProvider provider)
    {
        var context = new PermissionDefinitionContext();
        provider.Define(context);
        return context;
    }

    [Theory]
    [InlineData("organization.view")]
    [InlineData("organization.create")]
    [InlineData("organization.update")]
    [InlineData("organization.delete")]
    public void Declares_the_four_codes_byte_identically(string code)
    {
        var context = Define(new IdentityOrganizationPermissions());

        context.Permissions.ContainsKey(code).ShouldBeTrue();
        context.Permissions[code].ParentName.ShouldBe("identity");
    }

    [Fact]
    public void Declares_exactly_those_four_codes()
    {
        Define(new IdentityOrganizationPermissions()).Permissions.Count.ShouldBe(4);
    }

    /// <summary>
    /// 子模块<b>不</b>重复声明父组。<c>AddGroup</c> 是 first-wins 的，重复声明当场不会出错，
    /// 但目录门禁要求每个 provider 只声明自己的组 —— 而 <c>identity</c> 是父模块的，
    /// 重复声明会让组的显示名取决于模块加载顺序。
    /// </summary>
    [Fact]
    public void Does_not_redeclare_the_parents_group()
    {
        Define(new IdentityOrganizationPermissions()).Groups.ShouldBeEmpty();
    }

    /// <summary>父模块交出这四个码之后不得再声明它们，否则两边撞码。</summary>
    [Theory]
    [InlineData("organization.view")]
    [InlineData("organization.create")]
    [InlineData("organization.update")]
    [InlineData("organization.delete")]
    public void Parent_no_longer_declares_them(string code)
    {
        Define(new IdentityPermissions()).Permissions.ContainsKey(code).ShouldBeFalse();
    }

    /// <summary>
    /// 对照组：父模块自己的码一个不少 —— 证明上面那条不是"父模块什么都不声明了"。
    /// </summary>
    [Fact]
    public void Parent_still_declares_its_own_codes()
    {
        var parent = Define(new IdentityPermissions());

        parent.Permissions.ContainsKey("user.view").ShouldBeTrue();
        parent.Permissions.ContainsKey("role.view").ShouldBeTrue();
        parent.Groups.ContainsKey("identity").ShouldBeTrue();
    }

    /// <summary>
    /// 模块必须真的把 provider 注册进容器。
    /// </summary>
    /// <remarks>
    /// 上面几条直接 <c>new</c> 出 provider 求值，永远经不过 DI —— 写好了权限类却忘了注册，
    /// 后果是 <c>PermissionDbSeeder</c> 收集不到它、码永远不被播种，于是每个管理端点恒 403，
    /// 而没有任何一处会告诉你为什么（2026-07-31 的 SigningModule 就是这么栽的）。
    /// </remarks>
    [Fact]
    public async Task Module_registers_the_provider()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(
            services,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        await new IdentityOrganizationModule().ConfigureServicesAsync(context);

        services.Where(d => d.ServiceType == typeof(IPermissionDefinitionProvider))
            .Select(d => d.ImplementationType)
            .ShouldContain(typeof(IdentityOrganizationPermissions));
    }

    /// <summary>
    /// 模块必须注册 <c>IOrganizationService</c> 的实现 —— 契约在核心，实现只可能来自这里。
    /// </summary>
    [Fact]
    public async Task Module_registers_the_organization_service()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(
            services,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        await new IdentityOrganizationModule().ConfigureServicesAsync(context);

        services.Where(d => d.ServiceType == typeof(IOrganizationService))
            .Select(d => d.ImplementationType)
            .ShouldContain(typeof(OrganizationService));
    }
}
