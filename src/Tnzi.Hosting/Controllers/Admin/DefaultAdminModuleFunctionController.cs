namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员模块功能管理控制器
/// 路由：/api/admin/module-functions（自动添加 /api 前缀）
/// 用户项目可以实现 AdminModuleFunctionController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/module-functions")]
public class DefaultAdminModuleFunctionController : ModuleFunctionAdminControllerBase
{
    public DefaultAdminModuleFunctionController(IModuleManagementService moduleManagementService)
        : base(moduleManagementService)
    {
    }
}