using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Mvc;
using Tnzi.Authorization.DataAuth.Controllers.Admin;

namespace Tnzi.Authorization.DataAuth.Tests.Controllers;

/// <summary>
/// 数据授权 admin 控制器的门与路由契约（拆分前住在 <c>Tnzi.Authorization.Tests</c> 的
/// <c>AdminGateCompositionTests</c> 里）。
/// </summary>
/// <remarks>
/// 权限码与路由模板<b>都不许因为换了程序集而改动</b>：码变了既有角色授权行全部失效，
/// 路由变了前端与所有直接调 HTTP 的消费方一起断。这两条正是行为测试（mock 服务层）
/// 全绿也照样能塌掉的地方。
/// </remarks>
public class DataAuthAdminControllerGateTests
{
    private static List<string?> ClassPermissionNames(Type controllerType)
        => controllerType
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: true)
            .Cast<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .Where(p => p != null)
            .ToList();

    private static List<string?> MethodPermissionNames(string methodName)
        => typeof(DefaultDataAuthAdminController)
            .GetMethod(methodName)!
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: true)
            .Cast<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .ToList();

    [Fact]
    public void Controller_carries_its_class_level_module_code()
    {
        var names = ClassPermissionNames(typeof(DefaultDataAuthAdminController));

        names.ShouldHaveSingleItem(
            "DefaultDataAuthAdminController must carry exactly its class-level module code "
            + "(the base contributes only the bare authentication gate).");
        names[0].ShouldBe("authorization.entityRole.view");
    }

    [Theory]
    [InlineData("CreateEntityInfo", "authorization.entityRole.create")]
    [InlineData("UpdateEntityInfo", "authorization.entityRole.update")]
    [InlineData("DeleteEntityInfo", "authorization.entityRole.delete")]
    [InlineData("CreateEntityRole", "authorization.entityRole.create")]
    [InlineData("UpdateEntityRole", "authorization.entityRole.update")]
    [InlineData("DeleteEntityRole", "authorization.entityRole.delete")]
    [InlineData("BatchCreateEntityRoles", "authorization.entityRole.create")]
    public void Write_endpoint_carries_its_method_level_action_code(string methodName, string expectedCode)
    {
        MethodPermissionNames(methodName).ShouldContain(expectedCode,
            $"{methodName} must carry the method-level action code (AND with the class-level .view gate).");
    }

    /// <summary>
    /// 路由模板逐字不变。子模块拿走的是<b>整个</b>控制器，父模块没有留下任何控制器还用着
    /// <c>admin/data-auth</c> —— 这是「整只控制器可以搬」的前提：若父模块还占着同一个模板，
    /// 消费方继承父模块默认控制器时会连带把子模块的端点一起带上
    /// （<c>[DefaultController]</c> 不继承，而 <c>[Route]</c> 继承）。
    /// </summary>
    [Fact]
    public void Route_template_is_unchanged()
    {
        var route = typeof(DefaultDataAuthAdminController)
            .GetCustomAttribute<RouteAttribute>(inherit: false);

        route.ShouldNotBeNull();
        route!.Template.ShouldBe("admin/data-auth");
    }

    /// <summary>父模块的程序集里不该再有任何控制器占用 <c>admin/data-auth</c>。</summary>
    [Fact]
    public void Parent_assembly_no_longer_serves_the_route()
    {
        var parentRoutes = typeof(AuthorizationModule).Assembly
            .GetTypes()
            .Where(t => typeof(ApiAdminControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.GetCustomAttribute<RouteAttribute>(inherit: false)?.Template)
            .Where(t => t != null)
            .ToList();

        parentRoutes.ShouldNotBeEmpty("父模块仍然有 admin 控制器 —— 扫描空了的话这条断言恒真。");
        parentRoutes.ShouldNotContain("admin/data-auth");
    }
}
