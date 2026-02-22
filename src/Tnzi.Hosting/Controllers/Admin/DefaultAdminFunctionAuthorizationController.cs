using IFunctionAuthorizationService = Tnzi.Authorization.Services.IFunctionAuthorizationService;

namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员功能授权控制器
/// 路由：/api/admin/function-authorization（自动添加 /api 前缀）
/// 用户项目可以实现 AdminFunctionAuthorizationController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/function-authorization")]
public class DefaultAdminFunctionAuthorizationController : FunctionAuthorizationAdminControllerBase
{
    public DefaultAdminFunctionAuthorizationController(
        IFunctionAuthorizationService functionAuthorizationService,
        IModuleManagementService moduleManagementService)
        : base(functionAuthorizationService, moduleManagementService)
    {
    }
}