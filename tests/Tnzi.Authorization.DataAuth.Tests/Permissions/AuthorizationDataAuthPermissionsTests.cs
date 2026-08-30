using Tnzi.Authorization.Permissions;

namespace Tnzi.Authorization.DataAuth.Tests.Permissions;

/// <summary>
/// 四个 <c>authorization.entityRole.*</c> 码搬家不改名，父组仍是 <c>authorization</c>。
/// </summary>
/// <remarks>
/// 码串是<b>数据库里既有 <c>Auth_RoleFunction</c> 行的外键</b>（按 code 解析），改一个字符
/// 就等于把所有已授出的数据权限静默作废；而作废的表现不是报错，是「某天起这些人看不到那些行了」。
/// 生态级的总数锁与「每个码只能有一个声明模块」在
/// <c>tests/Tnzi.PermissionCatalogue.Tests</c>，这里守的是本包自己这一份。
/// </remarks>
public class AuthorizationDataAuthPermissionsTests
{
    private static PermissionDefinitionContext Define(IPermissionDefinitionProvider provider)
    {
        var context = new PermissionDefinitionContext();
        provider.Define(context);
        return context;
    }

    [Theory]
    [InlineData("authorization.entityRole.view")]
    [InlineData("authorization.entityRole.create")]
    [InlineData("authorization.entityRole.update")]
    [InlineData("authorization.entityRole.delete")]
    public void Declares_the_four_codes_byte_identically(string code)
    {
        var context = Define(new AuthorizationDataAuthPermissions());

        context.Permissions.ContainsKey(code).ShouldBeTrue();
        context.Permissions[code].ParentName.ShouldBe("authorization");
    }

    [Fact]
    public void Declares_exactly_those_four_codes()
    {
        var context = Define(new AuthorizationDataAuthPermissions());

        context.Permissions.Count.ShouldBe(4);
    }

    /// <summary>
    /// 子模块<b>不</b>重复声明父组。<c>AddGroup</c> 是 first-wins 的，重复声明当场不会出错，
    /// 但目录门禁要求每个 provider 只声明自己的组 —— 而 <c>authorization</c> 是父模块的。
    /// </summary>
    [Fact]
    public void Does_not_redeclare_the_parents_group()
    {
        Define(new AuthorizationDataAuthPermissions()).Groups.ShouldBeEmpty();
    }

    /// <summary>父模块交出这四个码之后不得再声明它们，否则两边撞码。</summary>
    [Theory]
    [InlineData("authorization.entityRole.view")]
    [InlineData("authorization.entityRole.create")]
    [InlineData("authorization.entityRole.update")]
    [InlineData("authorization.entityRole.delete")]
    public void Parent_no_longer_declares_them(string code)
    {
        var parent = Define(new AuthorizationPermissions());

        parent.Permissions.ContainsKey(code).ShouldBeFalse(
            $"'{code}' 现在归 Tnzi.Authorization.DataAuth 声明；父模块再声明一次就成了两个 owner。");
        // 扫描没塌：父模块仍然声明着它自己的功能级码。
        parent.Permissions.ContainsKey("authorization.roleFunction.view").ShouldBeTrue();
    }

    /// <summary>
    /// 类别随父组默认（Technical）。「谁能改数据范围」等于「谁能决定别人看得见哪几行」，
    /// 分配界面靠这个徽标提示这不是普通业务码 —— 拆分不该把它降级成 Business。
    /// </summary>
    [Fact]
    public void Category_is_unchanged_when_the_parent_group_is_present()
    {
        var context = new PermissionDefinitionContext();
        // 真实宿主里两个 provider 都会跑，父组先声明 defaultCategory: Technical。
        new AuthorizationPermissions().Define(context);
        new AuthorizationDataAuthPermissions().Define(context);

        context.Permissions["authorization.entityRole.view"].Category.ShouldBe(PermissionCategory.Technical);
    }
}
