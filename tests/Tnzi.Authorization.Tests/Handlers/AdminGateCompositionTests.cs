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
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultUserFunctionAdminController), "authorization.userFunction.view")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultModuleAdminController), "authorization.functionModule.view")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultModuleFunctionAdminController), "authorization.permission.view")]
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

    /// <summary>
    /// 角色授权 / 用户直授 / 权限目录三张页各自只要求自己的类级 .view 码,它们要读的目录与角色列表
    /// 就必须挂在同一个控制器上、不再叠加方法级码。此前目录只在 admin/modules(functionModule.view)下,
    /// 只持页面码的委托管理员打开页面得到 403 与一张空矩阵。
    /// </summary>
    [Theory]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultRoleFunctionAdminController), "GetCatalogue", "catalogue")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultRoleFunctionAdminController), "GetRoles", "roles")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultUserFunctionAdminController), "GetCatalogue", "catalogue")]
    [InlineData(typeof(Tnzi.Authorization.Controllers.Admin.DefaultModuleFunctionAdminController), "GetCatalogue", "catalogue")]
    public void Page_reads_live_under_the_page_controller_gate(Type controllerType, string methodName, string template)
    {
        var method = controllerType.GetMethod(methodName);
        method.ShouldNotBeNull($"{controllerType.Name} must expose {methodName} so its page needs only the class-level code.");

        var get = method!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Mvc.HttpGetAttribute>()
            .ShouldHaveSingleItem();
        get.Template.ShouldBe(template);

        MethodPermissionNames(controllerType, methodName).Where(p => p != null).ShouldBeEmpty(
            $"{controllerType.Name}.{methodName} must be readable with the class-level .view code alone.");
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

    [Fact]
    // 收窄后的豁免面:除两个自服务端点外,这个控制器上的每一个动作都携带 authorization.permission.view。
    // 按反射枚举全部动作而不是逐个列名字:列名字的版本守不住「新加一个端点忘了加门」,
    // 它只会在被列出的那几个上变红。
    public void Narrowed_self_service_reads_carry_permission_view()
    {
        var controllerType = typeof(Tnzi.Authorization.Controllers.Admin.DefaultFunctionAuthorizationAdminController);
        string[] selfService = ["GetUserPermissionNames", "GetAccessProfile"];
        var actions = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), inherit: true).Length > 0)
            .Select(m => m.Name)
            .ToList();

        // 枚举本身必须看得见这些动作,否则下面的循环对空集恒真。
        actions.ShouldContain("CheckPermission");
        actions.ShouldContain("GetPermissionHolders");
        actions.ShouldContain("GetStatistics");
        actions.ShouldContain("GetUserPermissionNames");

        foreach (var action in actions.Where(a => !selfService.Contains(a)))
        {
            MethodPermissionNames(controllerType, action)
                .ShouldHaveSingleItem($"{action} must carry exactly the method-level authorization.permission.view gate.")
                .ShouldBe("authorization.permission.view");
        }
    }
}
