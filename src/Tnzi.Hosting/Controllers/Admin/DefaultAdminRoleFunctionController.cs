namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员角色功能管理控制器
/// 路由：/api/admin/role-functions（自动添加 /api 前缀）
/// 用户项目可以实现 AdminRoleFunctionController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/role-functions")]
public class DefaultAdminRoleFunctionController : RoleFunctionAdminControllerBase
{
    public DefaultAdminRoleFunctionController(IRoleFunctionService roleFunctionService)
        : base(roleFunctionService)
    {
    }
}