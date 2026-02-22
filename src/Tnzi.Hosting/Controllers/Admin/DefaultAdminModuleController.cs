namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员模块管理控制器
/// 路由：/api/admin/modules（自动添加 /api 前缀）
/// 用户项目可以实现 AdminModuleController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/modules")]
public class DefaultAdminModuleController : ModuleAdminControllerBase
{
    public DefaultAdminModuleController(IModuleManagementService moduleManagementService)
        : base(moduleManagementService)
    {
    }
}