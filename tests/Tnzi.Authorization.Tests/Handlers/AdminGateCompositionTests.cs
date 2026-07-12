using System.Reflection;

namespace Tnzi.Authorization.Tests.Handlers;

/// <summary>
/// 锁定 admin 门的静态合成契约。这是权限模型最高风险的回归面:
/// <list type="bullet">
///   <item><see cref="ApiAuthorizeAttribute"/> 必须 <c>AllowMultiple = true</c>——
///     否则派生类/方法级特性会*替换*基类特性(CLR 对 AllowMultiple=false 的
///     继承特性只保留最派生的一个),AND 门静默塌层,而所有行为测试
///     (mock 服务层)依旧全绿。</item>
///   <item>基类 <c>ApiAdminControllerBase</c> 只提供认证边界(裸门,无权限码)
///     ——进门/落脚点是基线基础设施,不是权限矩阵里可被清空的行;</item>
///   <item>代表性 admin 控制器必须携带类级模块码;自服务豁免控制器不携带
///     任何权限码(仅认证)。</item>
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
            "AllowMultiple=false would make a method-level action-code attribute REPLACE " +
            "the class-level module code instead of AND-composing with it.");
        usage.Inherited.ShouldBeTrue(
            "Inherited=false would drop the ApiAdminControllerBase authentication boundary entirely.");
    }

    [Fact]
    public void Admin_base_class_is_an_authentication_boundary_without_a_permission_code()
    {
        // 基线基础设施不可授予/不可清空:基类不得再压任何权限码(旧
        // Admin.Manage 外层门的事故面=清空角色后其成员连权限自查都 403,
        // 前端登录链路死锁)。真实门禁由各控制器的类级 .view + 方法级操作码承担。
        var baseAttrs = typeof(Tnzi.AspNetCore.Mvc.ApiAdminControllerBase)
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: false)
            .Cast<ApiAuthorizeAttribute>()
            .ToList();

        baseAttrs.Count.ShouldBe(1);
        baseAttrs[0].PermissionName.ShouldBeNull(
            "ApiAdminControllerBase must require authentication only - a permission code here becomes an un-clearable baseline row in the matrix.");
    }

    [Theory]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultRoleFunctionAdminController), "authorization.roleFunction.view")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultModuleAdminController), "authorization.functionModule.view")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultModuleFunctionAdminController), "authorization.permission.view")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultDataAuthAdminController), "authorization.entityRole.view")]
    public void Gated_admin_controller_carries_its_module_code(Type controllerType, string moduleCode)
    {
        var permissionNames = controllerType
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: true)
            .Cast<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .Where(p => p != null)
            .ToList();

        permissionNames.ShouldHaveSingleItem(
            $"{controllerType.Name} must carry exactly its class-level module code (the base contributes only the bare authentication gate).");
        permissionNames[0].ShouldBe(moduleCode);
    }

    [Fact]
    public void Self_service_controller_carries_no_class_level_code()
    {
        // 前端登录链路自服务端点(用户权限名列表/访问档案)刻意不加类级模块码——
        // 加了就把所有后台用户锁在"查不到自己有哪些权限"之外。管理读端点改由
        // 方法级 authorization.permission.view 逐一把守(见下一个测试)。
        var permissionNames = typeof(Tnzi.Authorization.Controllers.Admin.DefaultFunctionAuthorizationAdminController)
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: true)
            .Cast<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .Where(p => p != null)
            .ToList();

        permissionNames.ShouldBeEmpty();
    }

    // ── 方法级操作码(端点级强制)静态契约 ──────────────────────────────────
    // 类级 .view 码只回答"能否到达该管理面";写端点必须再携带各自的操作码
    // (AND 语义)。这里锁定代表性端点,防止有人删掉方法级特性后行为测试
    // (mock 服务层)依旧全绿。

    private static List<string?> MethodPermissionNames(Type controllerType, string methodName)
        => controllerType
            .GetMethod(methodName)!
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: true)
            .Cast<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .ToList();

    [Theory]
    // 角色授权写端点统一走专用 assign 动作码——委托护栏的静态孪生。
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultRoleFunctionAdminController), "AssignFunctionsToRole", "authorization.roleFunction.assign")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultRoleFunctionAdminController), "SetRoleFunctions", "authorization.roleFunction.assign")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultRoleFunctionAdminController), "ClearRoleFunctions", "authorization.roleFunction.assign")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultModuleAdminController), "Create", "authorization.functionModule.create")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultModuleFunctionAdminController), "Delete", "authorization.permission.delete")]
    public void Write_endpoint_carries_its_method_level_action_code(Type controllerType, string methodName, string expectedCode)
    {
        MethodPermissionNames(controllerType, methodName).ShouldContain(expectedCode,
            $"{controllerType.Name}.{methodName} must carry the method-level action code (AND with the class-level .view gate).");
    }

    [Theory]
    // 自服务端点必须保持裸(仅随基类认证边界开放,无任何权限码)——登录链路依赖。
    [InlineData("GetUserPermissionNames")]
    [InlineData("GetAccessProfile")]
    public void Self_service_endpoints_stay_bare(string methodName)
    {
        MethodPermissionNames(
            typeof(Tnzi.Authorization.Controllers.Admin.DefaultFunctionAuthorizationAdminController), methodName)
            .ShouldBeEmpty($"{methodName} is on the login chain and must not require any module/action code.");
    }

    [Theory]
    // 收窄后的豁免面:其余管理读端点逐一携带 authorization.permission.view。
    [InlineData("CheckPermission")]
    [InlineData("GetModuleTree")]
    [InlineData("GetModuleFunctions")]
    [InlineData("GetPermissionRoles")]
    [InlineData("GetPermissionUsers")]
    [InlineData("GetStatistics")]
    public void Narrowed_self_service_reads_carry_permission_view(string methodName)
    {
        MethodPermissionNames(
            typeof(Tnzi.Authorization.Controllers.Admin.DefaultFunctionAuthorizationAdminController), methodName)
            .ShouldBe(new[] { "authorization.permission.view" });
    }
}
