using System.Reflection;

namespace Tnzi.Authorization.Tests.Handlers;

/// <summary>
/// 锁定两层 admin 门的静态合成契约。这是整个两档管理员模型最高风险的回归面:
/// <list type="bullet">
///   <item><see cref="ApiAuthorizeAttribute"/> 必须 <c>AllowMultiple = true</c>——
///     否则派生类的模块码特性会*替换*基类 <c>ApiAdminControllerBase</c> 的
///     <c>Admin.Manage</c>(CLR 对 AllowMultiple=false 的继承特性只保留最派生的
///     一个),两层门静默塌成一层,而所有行为测试(mock 服务层)依旧全绿。</item>
///   <item>代表性 admin 控制器必须同时携带外层门 + 模块码(AND);
///     自服务豁免控制器必须只携带外层门。</item>
/// </list>
/// </summary>
public class AdminGateCompositionTests
{
    [Fact]
    public void ApiAuthorize_attribute_usage_allows_multiple_and_is_inherited()
    {
        var usage = typeof(ApiAuthorizeAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        usage.ShouldNotBeNull();
        usage!.AllowMultiple.ShouldBeTrue(
            "AllowMultiple=false would make a derived controller's module-code attribute REPLACE " +
            "the base Admin.Manage gate instead of AND-composing with it.");
        usage.Inherited.ShouldBeTrue(
            "Inherited=false would drop the ApiAdminControllerBase Admin.Manage gate entirely.");
    }

    [Theory]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultRoleFunctionAdminController), "authorization.roleFunction.view")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultModuleAdminController), "authorization.functionModule.view")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultModuleFunctionAdminController), "authorization.permission.view")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultDataAuthAdminController), "authorization.entityRole.view")]
    public void Gated_admin_controller_carries_both_outer_gate_and_module_code(Type controllerType, string moduleCode)
    {
        var permissionNames = controllerType
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: true)
            .Cast<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .ToList();

        permissionNames.Count.ShouldBe(2,
            $"{controllerType.Name} must carry exactly the base Admin.Manage gate plus its module code (AND).");
        permissionNames.ShouldContain("Admin.Manage");
        permissionNames.ShouldContain(moduleCode);
    }

    [Fact]
    public void Self_service_controller_keeps_only_the_outer_gate()
    {
        // 前端登录链路自服务端点(用户权限名列表/模块树)刻意不加模块码——
        // 加了就把所有后台用户锁在"查不到自己有哪些权限"之外。
        var permissionNames = typeof(Tnzi.Authorization.Controllers.Admin.DefaultFunctionAuthorizationAdminController)
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: true)
            .Cast<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .ToList();

        permissionNames.ShouldBe(new[] { "Admin.Manage" });
    }
}
