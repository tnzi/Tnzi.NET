using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.Authorization.Tests;

/// <summary>
/// 只加载 <c>Tnzi.Authorization</c>、没有加载 <c>Tnzi.Authorization.DataAuth</c> 时的现场。
/// </summary>
/// <remarks>
/// <para>
/// 本测试项目<b>刻意只引用父模块</b>，所以进程里根本没有那个子模块 —— 这就是消费方少加载
/// 一个包时的真实情况，不需要搭夹具去模拟。
/// </para>
/// <para>
/// ★ 要守的不变量是<b>「少能力，不是错行为」</b>。行级数据授权少掉，功能级授权必须逐字不变：
/// 权限判定运行时、策略提供程序、权限码目录、角色授权与用户直授一个不少。真正危险的失效形态
/// 不是「查不到数据范围」，而是「数据范围的过滤器安静消失、所有人看见全表」—— 而那件事在这里
/// 不可能发生：行级过滤只在消费方<b>显式调用</b>时才发生（<c>WithDataAuthAsync</c> /
/// <c>GetDataFilterAsync</c>），少了这个包，调用点连编译都过不去。本文件因此断言的是
/// 「父模块这一侧确实什么都没剩下」，而不是某个运行时回退的行为。
/// </para>
/// </remarks>
public class DataAuthPackageAbsenceTests
{
    private static Assembly ParentAssembly => typeof(AuthorizationModule).Assembly;

    /// <summary>四个 <c>authorization.entityRole.*</c> 码不再由父模块声明，因此不会被 seed。</summary>
    /// <remarks>
    /// 它们要是留在父模块，不加载子模块的宿主会在权限矩阵里看到一块「数据权限」，
    /// 授出去之后点进菜单是 404 —— 把「授权成功」和「功能可用」拆成两件事，
    /// 这正是权限目录随模块走要避免的东西。
    /// </remarks>
    [Theory]
    [InlineData("authorization.entityRole.view")]
    [InlineData("authorization.entityRole.create")]
    [InlineData("authorization.entityRole.update")]
    [InlineData("authorization.entityRole.delete")]
    public void EntityRoleCodes_AreNotSeededWithoutThePackage(string code)
    {
        var context = new PermissionDefinitionContext();
        new AuthorizationPermissions().Define(context);

        context.Permissions.ContainsKey(code).ShouldBeFalse(
            $"'{code}' 随 Tnzi.Authorization.DataAuth 走；父模块单独加载时不该 seed 它。");
    }

    /// <summary>功能级授权的权限码<b>一个不少</b> —— 这是「只少了行级那一块」的证据。</summary>
    [Theory]
    [InlineData("authorization.view")]
    [InlineData("authorization.functionModule.view")]
    [InlineData("authorization.permission.view")]
    [InlineData("authorization.roleFunction.view")]
    [InlineData("authorization.roleFunction.assign")]
    [InlineData("authorization.userFunction.view")]
    [InlineData("authorization.userFunction.assign")]
    [InlineData("authorization.dualControl.view")]
    [InlineData("authorization.dualControl.approve")]
    public void FunctionLevelCodes_AreUnaffected(string code)
    {
        var context = new PermissionDefinitionContext();
        new AuthorizationPermissions().Define(context);

        context.Permissions.ContainsKey(code).ShouldBeTrue(
            $"'{code}' 是功能级授权的码，与行级数据授权的拆分无关。");
    }

    /// <summary>父组仍由父模块声明 —— 子模块只借这棵树挂自己的码，不拥有它。</summary>
    [Fact]
    public void ParentStillOwnsTheAuthorizationGroup()
    {
        var context = new PermissionDefinitionContext();
        new AuthorizationPermissions().Define(context);

        context.Groups.Count.ShouldBe(1);
        context.Groups.ContainsKey("authorization").ShouldBeTrue();
    }

    /// <summary>
    /// <c>admin/data-auth</c> 的十五个端点整体消失：父模块里没有任何控制器占用这个模板。
    /// </summary>
    /// <remarks>
    /// 「整只控制器搬走」是这条拆分线成立的前提。若父模块还留着一个同模板的控制器，
    /// 消费方继承它做定制时会连带把子模块的端点一起带上 ——
    /// <c>[DefaultController]</c> 不继承而 <c>[Route]</c> 继承，两者的差异正好制造这种意外。
    /// </remarks>
    [Fact]
    public void DataAuthRoute_IsNotServedByTheParent()
    {
        var templates = ParentAssembly.GetTypes()
            .Where(t => typeof(Tnzi.AspNetCore.Mvc.ApiAdminControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.GetCustomAttribute<RouteAttribute>(inherit: false)?.Template)
            .Where(t => t != null)
            .ToList();

        // 非空洞守卫：扫不到控制器时下面那条断言恒真。
        templates.Count.ShouldBeGreaterThanOrEqualTo(5,
            $"只扫到 {templates.Count} 个 admin 控制器路由 —— 是扫描坏了，不是控制器真的没了。");
        templates.ShouldNotContain("admin/data-auth");
        // 功能级授权的管理面照旧。
        templates.ShouldContain("admin/role-functions");
    }

    /// <summary>行级数据授权的契约与实现<b>都不在</b>父程序集里。</summary>
    /// <remarks>
    /// 契约刻意<b>没有</b>留在父模块：拆分前父模块对这一簇只有一处引用（那行 DI 注册），
    /// 没有任何一段父模块代码需要向数据范围提问，所以留一个契约 + 可选注入只是多一层空转。
    /// 这条断言把那个判断钉住 —— 哪天父模块又开始需要这个事实，它会先在这里变红，
    /// 提醒人把契约提回父模块（或核心）再让消费方可选注入，而不是顺手加一条反向依赖。
    /// </remarks>
    [Theory]
    [InlineData("IDataAuthService")]
    [InlineData("DataAuthService")]
    [InlineData("EntityInfo")]
    [InlineData("EntityRole")]
    [InlineData("DataAuthOperation")]
    public void DataAuthTypes_AreGoneFromTheParentAssembly(string typeName)
    {
        var hit = ParentAssembly.GetTypes().Any(t => t.Name == typeName);

        hit.ShouldBeFalse($"{typeName} 应该只存在于 Tnzi.Authorization.DataAuth 里。");
    }

    /// <summary>父程序集里不再有 <c>WithDataAuthAsync</c> 扩展方法。</summary>
    /// <remarks>
    /// 它是行级过滤唯一的消费入口，也是「忘了加载这个包」为什么表现为编译错误而不是
    /// 静默放行的原因：方法没了，调用点就编译不过，不存在「过滤器悄悄失效」的窗口。
    /// </remarks>
    [Fact]
    public void WithDataAuthAsync_IsGoneFromTheParentAssembly()
    {
        var hit = ParentAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Any(m => m.Name == "WithDataAuthAsync");

        hit.ShouldBeFalse();
    }

    /// <summary>
    /// 父模块的 <c>ConfigureServicesAsync</c> 不再注册任何数据授权服务，
    /// 而功能级授权的注册一条不少。
    /// </summary>
    [Fact]
    public async Task ParentModuleRegistersFunctionLevelServicesButNoDataAuth()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();

        await new AuthorizationModule()
            .ConfigureServicesAsync(new ServiceConfigurationContext(services, configuration));

        var serviceTypeNames = services.Select(d => d.ServiceType.Name).ToList();

        serviceTypeNames.ShouldNotContain("IDataAuthService");
        // 非空洞守卫 + 「只少了这一块」的证据。
        serviceTypeNames.ShouldContain(nameof(IUserFunctionService));
        serviceTypeNames.ShouldContain(nameof(IDualControlService));
        serviceTypeNames.ShouldContain(nameof(Tnzi.Security.Authorization.IPermissionChecker));
    }
}
